using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using WinForms = System.Windows.Forms;
using SmartMacroAI.Localization;
using SmartMacroAI.Models;

namespace SmartMacroAI.Core;

/// <summary>Runtime macro variables (brace placeholders <c>{name}</c>); cleared when a script run starts.</summary>
public sealed class VariableManager
{
    private readonly Dictionary<string, object> _vars = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string name, object value) => _vars[name.Trim()] = value;

    public object? Get(string name) =>
        _vars.TryGetValue(name.Trim(), out object? v) ? v : null;

    public int GetInt(string name, int defaultVal = 0) =>
        _vars.TryGetValue(name.Trim(), out object? v) && v is int i ? i :
        v is long l ? (int)l :
        int.TryParse(v?.ToString(), out int parsed) ? parsed : defaultVal;

    public string GetString(string name, string defaultVal = "") =>
        _vars.TryGetValue(name.Trim(), out object? v) ? v?.ToString() ?? defaultVal : defaultVal;

    public void Increment(string name, int amount = 1)
    {
        int current = GetInt(name, 0);
        Set(name, current + amount);
    }

    public void Clear() => _vars.Clear();

    public void Remove(string name) => _vars.Remove(name.Trim());

    /// <summary>Replace <c>{varName}</c> with each stored value (string form).</summary>
    public string Resolve(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        foreach (var kv in _vars.ToList())
        {
            string key = kv.Key;
            string token = "{" + key + "}";
            input = input.Replace(token, kv.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
        }

        return input;
    }

    public string DumpAll() =>
        string.Join(", ", _vars.Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>Copies all stored values as strings into <paramref name="target"/> (for <c>{{ }}</c> interpolation).</summary>
    public void CopyStringValuesInto(IDictionary<string, string> target)
    {
        foreach (var kv in _vars)
            target[kv.Key] = kv.Value?.ToString() ?? string.Empty;
    }

    public IEnumerable<KeyValuePair<string, string>> EnumerateStringVariables() =>
        _vars.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value?.ToString() ?? string.Empty));
}

/// <summary>
/// Asynchronous macro execution engine.
/// Runs entirely on background threads — the WPF UI thread is never blocked.
/// All window interactions go through <see cref="Win32Api"/> (PostMessage / SendMessage),
/// which means the physical mouse and keyboard are NEVER hijacked.
/// Web steps use <see cref="PlaywrightEngine"/> (headful browser) in parallel with desktop actions.
/// </summary>
public sealed class MacroEngine
{
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(
        uint wVirtKey, uint wScanCode,
        byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszBuff,
        int cchBuff, uint wFlags, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    private const uint WM_CHAR  = 0x0102;
    private const uint WM_CUT   = 0x0300;
    private const uint WM_COPY  = 0x0301;
    private const uint WM_PASTE = 0x0302;
    private const uint WM_UNDO  = 0x0304;
    private const uint EM_SETSEL = 0x00B1;

    private PlaywrightEngine? _playwrightEngine;

    private readonly BezierMouseMover _mouseMover = new();

    private readonly VariableStore _variableStore = new();

    private OcrService _ocrService = new();

    private readonly MacroRunner _macroRunner = new();

    private readonly BehaviorRandomizerState _behaviorState = new();

    private int _currentMacroIteration;

    /// <summary>Browser mode for web actions (default <see cref="BrowserMode.Internal"/>).</summary>
    public BrowserMode BrowserMode { get; set; } = BrowserMode.Internal;

    /// <summary>
    /// CSV column name to look up in each row for an AdsPower profile ID.
    /// When set and a matching column exists in the current row, the browser
    /// is launched with that profile for the duration of the row.
    /// </summary>
    public string? CsvProfileIdColumn { get; set; }

    /// <summary>Current AdsPower profile ID (updated per CSV row).</summary>
    private string? _currentProfileId;

    private string _lastOcrText = string.Empty;

    private double _lastImageMatchConfidence;

    /// <summary>Thread-safe string variables (<c>{{name}}</c>) for the current run; UI may snapshot while a macro runs.</summary>
    public VariableStore RuntimeStringVariables => _variableStore;

    /// <summary>
    /// Data-driven CSV rows loaded from the UI (CsvDataService).
    /// When set, the engine runs one macro pass per row, injecting each row's
    /// columns into the runtime variable map under their normalized header keys.
    /// </summary>
    public List<Dictionary<string, string>>? DataRows { get; set; }

    /// <summary>Merged view for the Dashboard variables panel (engine + string store).</summary>
    public IReadOnlyList<(string Name, string Value, string Source)> GetLiveVariableRows()
    {
        var d = new Dictionary<string, (string V, string S)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _vars.EnumerateStringVariables())
            d[kv.Key] = (kv.Value, "Engine");
        foreach (var kv in _variableStore.Snapshot())
            d[kv.Key] = (kv.Value.Value, kv.Value.Source);
        return d.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => (x.Key, x.Value.V, x.Value.S))
            .ToList();
    }

    /// <summary>
    /// Current Win32 target for desktop actions. Updated when <see cref="LaunchAndBindAction"/> runs.
    /// </summary>
    private IntPtr _runtimeTargetHwnd;

    /// <summary>
    /// When true, desktop input may use low-level injection (e.g. SendInput) where implemented.
    /// Set by the UI for DirectInput-heavy games; default is false (message-based input).
    /// </summary>
    public bool HardwareMode { get; set; }

    /// <summary>
    /// Merged script variables + optional per-iteration CSV columns (case-insensitive keys).
    /// </summary>
    private Dictionary<string, string> _runtimeVariables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional callback after <see cref="LaunchAndBindAction"/> resolves a new HWND (e.g. Dashboard stealth).
    /// </summary>
    public Action<IntPtr>? TargetWindowRebound { get; set; }

    private readonly VariableManager _vars = new();

    private readonly StringBuilder _runReport = new();

    private int _totalRowsTotal;
    private int _totalRowsDone;
    private DateTime _sessionStartTime;

    /// <summary>Creates an engine instance and wires Bézier mouse diagnostics to <see cref="Log"/>.</summary>
    public MacroEngine()
    {
        _mouseMover.DiagnosticLog = msg => Log?.Invoke(msg);
    }

    // ═══════════════════════════════════════════════
    //  EVENTS — for UI progress/logging
    // ═══════════════════════════════════════════════

    public event Action<string>? Log;
    public event Action<MacroAction, int>? ActionStarted;
    public event Action<MacroAction, int>? ActionCompleted;
    public event Action<int, int>? IterationStarted;
    public event Action<int, int>? DataRowCompleted;
    public event Action? ExecutionFinished;
    public event Action<Exception>? ExecutionFaulted;

    private void OnLog(string message) => Log?.Invoke(message);

    /// <summary>
    /// Returns true if the script (including nested IF branches) contains a
    /// <see cref="LaunchAndBindAction"/> so execution may start with HWND deferred.
    /// </summary>
    public static bool ScriptContainsLaunchAndBind(MacroScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        foreach (var action in script.Actions)
        {
            if (ActionTreeContainsLaunchAndBind(action))
                return true;
        }

        return false;
    }

    private static bool ActionTreeContainsLaunchAndBind(MacroAction action) => action switch
    {
        LaunchAndBindAction => true,
        IfImageAction img => AnyNestedLaunch(img.ThenActions) || AnyNestedLaunch(img.ElseActions),
        IfTextAction txt => AnyNestedLaunch(txt.ThenActions) || AnyNestedLaunch(txt.ElseActions),
        RepeatAction rep => AnyNestedLaunch(rep.LoopActions),
        TryCatchAction tc => AnyNestedLaunch(tc.TryActions) || AnyNestedLaunch(tc.CatchActions),
        IfVariableAction iv => AnyNestedLaunch(iv.ThenActions) || AnyNestedLaunch(iv.ElseActions),
        SetVariableAction or LogAction => false,
        _ => false,
    };

    private static bool AnyNestedLaunch(List<MacroAction> list)
    {
        foreach (var a in list)
        {
            if (ActionTreeContainsLaunchAndBind(a))
                return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Executes a <see cref="MacroScript"/> against the window whose title
    /// matches <see cref="MacroScript.TargetWindowTitle"/>.
    /// Runs asynchronously on background threads — safe to call from the UI.
    /// </summary>
    public async Task ExecuteScriptAsync(MacroScript script, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(script);

        bool hasLaunch = ScriptContainsLaunchAndBind(script);
        var compat = MacroScriptValidation.ValidateScriptCompatibility(script);
        bool allowNoTarget = hasLaunch || compat.IsWebOnly || !compat.RequiresDesktopTarget;

        if (string.IsNullOrWhiteSpace(script.TargetWindowTitle))
        {
            if (!allowNoTarget)
            {
                throw new ArgumentException(
                    "TargetWindowTitle must be set before execution (or add a Launch & Bind action, or use only Web + Wait actions).",
                    nameof(script));
            }

            await ExecuteScriptAsync(script, IntPtr.Zero, token).ConfigureAwait(false);
            return;
        }

        IntPtr hwnd = Win32Api.FindWindowByPartialTitle(script.TargetWindowTitle);
        if (hwnd == IntPtr.Zero && allowNoTarget)
        {
            await ExecuteScriptAsync(script, IntPtr.Zero, token).ConfigureAwait(false);
            return;
        }

        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Target window not found: \"{script.TargetWindowTitle}\". " +
                "If the window is hidden via Stealth, use the pre-resolved HWND overload.");
        }

        await ExecuteScriptAsync(script, hwnd, token).ConfigureAwait(false);
    }

    /// <summary>
    /// Overload that accepts a pre-resolved HWND (useful when the
    /// UI has already identified the target window). HWND may be zero when the script
    /// includes <see cref="LaunchAndBindAction"/> (deferred bind).
    /// </summary>
    public async Task ExecuteScriptAsync(MacroScript script, IntPtr hwnd, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(script);

        bool hasLaunch = ScriptContainsLaunchAndBind(script);
        var compat = MacroScriptValidation.ValidateScriptCompatibility(script);
        bool webOnly = compat.IsWebOnly;
        bool valid = hwnd != IntPtr.Zero && Win32Api.IsWindow(hwnd);

        if (!valid && !hasLaunch && !webOnly && compat.RequiresDesktopTarget)
            throw new ArgumentException(
                "Invalid window handle (or add a Launch & Bind action to defer binding, or use only Web + Wait actions).",
                nameof(hwnd));

        if (valid)
        {
            _runtimeTargetHwnd = hwnd;
            string windowTitle = Win32Api.GetWindowTitle(hwnd);
            OnLog($"Target acquired: \"{windowTitle}\" (HWND=0x{hwnd:X}) — PostMessage (stealth)");
        }
        else
        {
            _runtimeTargetHwnd = IntPtr.Zero;
            if (webOnly)
                OnLog("Playwright-only macro — no Win32 target (HWND not required).");
            else if (!compat.RequiresDesktopTarget)
                OnLog("No desktop target — script uses web / system / wait actions only.");
            else
                OnLog("Target window deferred — will bind when \"Launch & Bind\" runs.");
        }

        _vars.Clear();
        _variableStore.Clear();
        _lastOcrText = string.Empty;
        _lastImageMatchConfidence = 0;
        _runReport.Clear();
        _totalRowsTotal = 0;
        _totalRowsDone = 0;
        _sessionStartTime = DateTime.Now;

        _ocrService = new OcrService(AppSettings.Load().OcrLanguageTag);

        _mouseMover.ReloadFromAppSettings();
        _mouseMover.SetRawInputNotifyWindow(_runtimeTargetHwnd);

        _macroRunner.Timing.ResetSession();
        _behaviorState.Reset();
        Win32MouseInput.UseAntiDetectionMouseStyle = AppSettings.Load().AntiDetectionEnabled;

        CancellationTokenSource? autoStopCts = null;
        CancellationToken loopToken = token;
        if (script.AutoStopMinutes > 0)
        {
            autoStopCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            autoStopCts.CancelAfter(TimeSpan.FromMinutes(script.AutoStopMinutes));
            loopToken = autoStopCts.Token;
        }

        try
        {
            try
            {
                await RunLoopAsync(script, loopToken).ConfigureAwait(false);
                OnLog("Execution completed successfully.");
                ExecutionFinished?.Invoke();
                FireTelegramCompletion(script, rowsDone: _totalRowsDone, total: _totalRowsDone, hasError: false, lastErrorMessage: null);
            }
            catch (OperationCanceledException)
            {
                if (autoStopCts is { IsCancellationRequested: true } && !token.IsCancellationRequested)
                    OnLog("Execution stopped (auto-stop timer elapsed).");
                else
                    OnLog("Execution cancelled by user.");
                throw;
            }
            catch (Exception ex)
            {
                OnLog($"Execution faulted: {ex.Message}");
                ExecutionFaulted?.Invoke(ex);
                FireTelegramCompletion(script, rowsDone: _totalRowsDone, total: _totalRowsTotal, hasError: true, lastErrorMessage: ex.Message);
                throw;
            }
        }
        finally
        {
            try
            {
                await SaveRunReportIfAnyAsync(token).ConfigureAwait(false);
            }
            catch
            {
                // ignore secondary failures while tearing down
            }

            autoStopCts?.Dispose();
        }
    }

    private async Task SaveRunReportIfAnyAsync(CancellationToken token)
    {
        if (_runReport.Length == 0)
            return;

        try
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
            Directory.CreateDirectory(dir);
            string reportPath = Path.Combine(dir, $"Run_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            await File.WriteAllTextAsync(reportPath, _runReport.ToString(), token).ConfigureAwait(false);
            OnLog($"[Report saved: {reportPath}]");
        }
        catch (Exception ex)
        {
            OnLog($"[Report save failed: {ex.Message}]");
        }
        finally
        {
            _runReport.Clear();
        }
    }

    private string ExpandRuntime(string? s)
    {
        string t = MacroVariableInterpolator.Expand(s ?? "", _runtimeVariables);
        Action<string>? onMissing = key =>
            OnLog("    ⚠ Biến '{{" + key + "}}' chưa có giá trị — thay bằng chuỗi rỗng");
        for (int round = 0; round < 8; round++)
        {
            string prev = t;
            t = MacroVariableInterpolator.ExpandDoubleCurly(t, BuildDoubleCurlyDictionary(), round == 0 ? onMissing : null);
            if (string.Equals(prev, t, StringComparison.Ordinal))
                break;
        }

        return _vars.Resolve(t);
    }

    private Dictionary<string, string> BuildDoubleCurlyDictionary()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        d["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        d["loop_index"] = _currentMacroIteration.ToString(CultureInfo.InvariantCulture);
        d["last_ocr"] = _lastOcrText;
        d["last_image_match"] = _lastImageMatchConfidence.ToString("0.####", CultureInfo.InvariantCulture);
        foreach (var kv in _variableStore.Snapshot())
            d[kv.Key] = kv.Value.Value;
        _vars.CopyStringValuesInto(d);
        return d;
    }

    // ═══════════════════════════════════════════════
    //  REPEAT LOOP (data-driven CSV support)
    // ═══════════════════════════════════════════════

    private async Task RunLoopAsync(MacroScript script, CancellationToken token)
    {
        List<Dictionary<string, string>>? dataRows = DataRows;

        // ── Data-driven CSV mode ──────────────────────────
        if (dataRows is { Count: > 0 })
        {
            _totalRowsTotal = dataRows.Count;
            await RunDataDrivenLoopAsync(script, dataRows, script.LoopCsvSkipOnError, token)
                .ConfigureAwait(false);
            return;
        }

        // ── Standard repeat loop ───────────────────────────
        bool infinite = script.RepeatCount <= 0;
        _totalRowsTotal = infinite ? 1 : script.RepeatCount;
        int totalIterations = infinite ? int.MaxValue : script.RepeatCount;

        MacroScriptValidation.ValidateRepeatAndLoopCsv(script);

        for (int i = 1; i <= totalIterations; i++)
        {
            token.ThrowIfCancellationRequested();

            if (_runtimeTargetHwnd != IntPtr.Zero && !Win32Api.IsWindow(_runtimeTargetHwnd))
                throw new InvalidOperationException("Target window was closed during execution.");

            ApplyIterationVariables(script, i);
            _currentMacroIteration = i;

            string label = infinite ? $"#{i} (infinite)" : $"#{i}/{script.RepeatCount}";
            OnLog($"── Iteration {label} ──");
            IterationStarted?.Invoke(i, script.RepeatCount);

            await ExecuteActionsAsync(script.Actions, token).ConfigureAwait(false);
            _totalRowsDone++;

            bool hasMore = i < totalIterations;
            if (hasMore && script.IntervalMinutes > 0)
            {
                OnLog($"── Waiting {script.IntervalMinutes} min before next iteration ──");
                await Task.Delay(TimeSpan.FromMinutes(script.IntervalMinutes), token);
            }
        }
    }

    /// <summary>
    /// Runs one macro pass per CSV data row. Variables from the row are merged into
    /// the runtime map, then <c>ExecuteActionsAsync</c> runs the full action list.
    /// After each row the run log is flushed to <c>logs/run_{timestamp}.txt</c>.
    /// When <see cref="CsvProfileIdColumn"/> is set and a matching column exists in a row,
    /// the AdsPower browser is closed and restarted with that profile before execution.
    /// </summary>
    private async Task RunDataDrivenLoopAsync(
        MacroScript script,
        List<Dictionary<string, string>> dataRows,
        bool skipOnError,
        CancellationToken token)
    {
        int total = dataRows.Count;

        // Prepare CSV headers for runtime injection (normalized keys)
        var csvHeaderNames = new List<string>();
        foreach (var row in dataRows)
        {
            foreach (var key in row.Keys)
            {
                if (!csvHeaderNames.Contains(key, StringComparer.OrdinalIgnoreCase))
                    csvHeaderNames.Add(key);
            }
        }

        AppendRunLogHeader(script.Name, total, csvHeaderNames);

        // Reset per-row profile state
        string? previousProfileId = null;

        for (int rowIdx = 0; rowIdx < total; rowIdx++)
        {
            token.ThrowIfCancellationRequested();

            if (_runtimeTargetHwnd != IntPtr.Zero && !Win32Api.IsWindow(_runtimeTargetHwnd))
                throw new InvalidOperationException("Target window was closed during execution.");

            var row = dataRows[rowIdx];
            int rowNum = rowIdx + 1;

            // Merge static script variables, then overlay CSV row columns
            _runtimeVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (script.Variables is not null)
            {
                foreach (var kv in script.Variables)
                    _runtimeVariables[kv.Key] = kv.Value ?? "";
            }

            // CSV row values take precedence over static variables
            foreach (var kv in row)
                _runtimeVariables[kv.Key] = kv.Value;

            // AdsPower per-row profile switching
            string? rowProfileId = null;
            if (BrowserMode == BrowserMode.AdsPower && !string.IsNullOrWhiteSpace(CsvProfileIdColumn))
            {
                foreach (var key in row.Keys)
                {
                    if (string.Equals(key, CsvProfileIdColumn, StringComparison.OrdinalIgnoreCase))
                    {
                        rowProfileId = row[key];
                        break;
                    }
                }
            }

            // Switch profile if it changed (or first row with AdsPower)
            bool needsProfileSwitch =
                BrowserMode == BrowserMode.AdsPower &&
                !string.Equals(rowProfileId, previousProfileId, StringComparison.Ordinal);

            if (needsProfileSwitch)
            {
                // Close existing AdsPower browser
                if (_playwrightEngine is not null)
                {
                    try
                    {
                        await _playwrightEngine.StopAdsPowerProfileAsync(token).ConfigureAwait(false);
                        await _playwrightEngine.DisposeAsync().ConfigureAwait(false);
                    }
                    catch { }
                    _playwrightEngine = null;
                }

                if (!string.IsNullOrWhiteSpace(rowProfileId))
                {
                    OnLog($"  [AdsPower] Switching to profile: {rowProfileId}");
                    _playwrightEngine = new PlaywrightEngine
                    {
                        Mode = BrowserMode.AdsPower,
                        AdsPowerProfileId = rowProfileId,
                    };
                    _currentProfileId = rowProfileId;
                }
                else
                {
                    _currentProfileId = null;
                }
                previousProfileId = rowProfileId;
            }

            _currentMacroIteration = rowNum;
            OnLog($"── CSV Row {rowNum}/{total} ── {string.Join(", ", row.Select(kv => $"{kv.Key}={Truncate(kv.Value, 30)}"))}");
            IterationStarted?.Invoke(rowNum, total);

            try
            {
                await ExecuteActionsAsync(script.Actions, token).ConfigureAwait(false);
                _totalRowsDone++;
                string logLine = $"[{DateTime.Now:HH:mm:ss}] Row {rowNum}/{total} OK";
                AppendRunLog(logLine);
                OnLog($"  ✅ Row {rowNum}/{total} done");
            }
            catch (Exception ex) when (skipOnError)
            {
                _totalRowsDone++;
                string logLine = $"[{DateTime.Now:HH:mm:ss}] Row {rowNum}/{total} SKIPPED — {ex.Message}";
                AppendRunLog(logLine);
                OnLog($"  ⚠ Row {rowNum}/{total} skipped due to error: {ex.Message}");
                DataRowCompleted?.Invoke(rowNum, total);
            }

            DataRowCompleted?.Invoke(rowNum, total);

            if (rowIdx < total - 1 && script.IntervalMinutes > 0)
            {
                OnLog($"── Waiting {script.IntervalMinutes} min before next CSV row ──");
                await Task.Delay(TimeSpan.FromMinutes(script.IntervalMinutes), token);
            }
        }

        // Clean up AdsPower browser at the end
        if (_playwrightEngine is not null)
        {
            try
            {
                await _playwrightEngine.StopAdsPowerProfileAsync(token).ConfigureAwait(false);
                await _playwrightEngine.DisposeAsync().ConfigureAwait(false);
            }
            catch { }
            _playwrightEngine = null;
            _currentProfileId = null;
        }

        AppendRunLog($"[{DateTime.Now:HH:mm:ss}] === All {total} rows processed ===");
    }

    private static readonly string RunLogDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "logs");

    private static readonly string RunLogTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

    private static string? _currentRunLogPath;

    private void AppendRunLogHeader(string macroName, int totalRows, List<string> headers)
    {
        try
        {
            Directory.CreateDirectory(RunLogDir);
            _currentRunLogPath = Path.Combine(RunLogDir, $"run_{RunLogTimestamp}.txt");
            string header = $"SmartMacroAI Run Log — {macroName} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                            $"Rows: {totalRows}  |  Headers: {string.Join(", ", headers)}\n" +
                            new string('-', 60) + "\n";
            File.AppendAllText(_currentRunLogPath, header);
        }
        catch
        {
            // Non-critical — don't crash the macro for log failures
        }
    }

    private void AppendRunLog(string line)
    {
        if (string.IsNullOrEmpty(_currentRunLogPath))
            return;
        try
        {
            File.AppendAllText(_currentRunLogPath, line + Environment.NewLine);
        }
        catch
        {
            // Non-critical
        }
    }

    private void EnsureDesktopTargetBound()
    {
        if (_runtimeTargetHwnd == IntPtr.Zero || !Win32Api.IsWindow(_runtimeTargetHwnd))
        {
            throw new InvalidOperationException(
                "No desktop target window is bound yet. Run \"Launch & Bind\" before desktop actions, " +
                "or start the macro with a valid target window.");
        }
    }

    private void ApplyIterationVariables(MacroScript script, int iteration1Based)
    {
        _runtimeVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (script.Variables is not null)
        {
            foreach (var kv in script.Variables)
                _runtimeVariables[kv.Key] = kv.Value ?? "";
        }

        if (string.IsNullOrWhiteSpace(script.LoopCsvFilePath)
            || script.LoopCsvColumnNames is null
            || script.LoopCsvColumnNames.Count == 0)
            return;

        string csvPath = MacroVariableInterpolator.Expand(script.LoopCsvFilePath.Trim(), _runtimeVariables);
        if (!File.Exists(csvPath))
            throw new FileNotFoundException("Loop CSV file not found.", csvPath);

        List<string[]> rows = MacroCsvLoopHelper.ReadDataRows(csvPath, script.LoopCsvHasHeader);
        int idx = iteration1Based - 1;
        if (idx < 0 || idx >= rows.Count)
        {
            throw new InvalidOperationException(
                $"Loop CSV has {rows.Count} data row(s); iteration {iteration1Based} is out of range.");
        }

        string[] cells = rows[idx];
        for (int c = 0; c < script.LoopCsvColumnNames.Count && c < cells.Length; c++)
        {
            string name = script.LoopCsvColumnNames[c].Trim();
            if (name.Length > 0)
                _runtimeVariables[name] = cells[c] ?? "";
        }
    }

    private async Task ExecuteSystemActionAsync(SystemAction action, CancellationToken token)
    {
        string Ex(string? s) => ExpandRuntime(s);

        switch (action.Kind)
        {
            case SystemActionKind.CreateFolder:
            {
                string path = Ex(action.Path);
                if (string.IsNullOrWhiteSpace(path))
                {
                    OnLog("  System CreateFolder — SKIPPED (empty path)");
                    return;
                }

                OnLog($"  System CreateFolder \"{path}\"");
                await Task.Run(() => Directory.CreateDirectory(path), token).ConfigureAwait(false);
                break;
            }
            case SystemActionKind.CopyFile:
            {
                string src = Ex(action.SourcePath);
                string dst = Ex(action.DestinationPath);
                if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
                {
                    OnLog("  System CopyFile — SKIPPED (empty source or destination)");
                    return;
                }

                OnLog($"  System CopyFile \"{src}\" → \"{dst}\"");
                await Task.Run(() =>
                {
                    if (!File.Exists(src))
                        throw new FileNotFoundException("Copy source not found.", src);
                    string? destDir = Path.GetDirectoryName(dst);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);
                    File.Copy(src, dst, action.Overwrite);
                }, token).ConfigureAwait(false);
                break;
            }
            case SystemActionKind.MoveFile:
            {
                string src = Ex(action.SourcePath);
                string dst = Ex(action.DestinationPath);
                if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
                {
                    OnLog("  System MoveFile — SKIPPED (empty source or destination)");
                    return;
                }

                OnLog($"  System MoveFile \"{src}\" → \"{dst}\"");
                await Task.Run(() =>
                {
                    if (!File.Exists(src))
                        throw new FileNotFoundException("Move source not found.", src);
                    string? destDir = Path.GetDirectoryName(dst);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);
                    File.Move(src, dst, action.Overwrite);
                }, token).ConfigureAwait(false);
                break;
            }
            case SystemActionKind.DeleteFile:
            {
                string path = Ex(action.Path);
                if (string.IsNullOrWhiteSpace(path))
                {
                    OnLog("  System DeleteFile — SKIPPED (empty path)");
                    return;
                }

                OnLog($"  System DeleteFile \"{path}\" (recursiveDir={action.RecursiveDelete})");
                await Task.Run(() =>
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    else if (Directory.Exists(path))
                        Directory.Delete(path, action.RecursiveDelete);
                    else
                        throw new FileNotFoundException("Delete path not found.", path);
                }, token).ConfigureAwait(false);
                break;
            }
            default:
                OnLog($"  System — unknown kind: {action.Kind}");
                break;
        }
    }

    /// <summary>
    /// Runs one web step on the persistent Playwright page (editor “Test step” / selector check).
    /// </summary>
    public static Task TestWebActionAsync(
        string url,
        string selector,
        string actionType,
        string? textToType,
        CancellationToken cancellationToken = default)
    {
        var engine = new MacroEngine();
        return engine.ExecuteWebActionAsync(url, selector, actionType, textToType ?? "", cancellationToken);
    }

    private static bool EvaluateCondition(string left, string op, string right)
    {
        op = op.Trim();
        bool leftNum = double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out double l);
        bool rightNum = double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out double r);

        if (leftNum && rightNum)
        {
            return op switch
            {
                "==" => Math.Abs(l - r) < 1e-9,
                "!=" => Math.Abs(l - r) >= 1e-9,
                ">" => l > r,
                "<" => l < r,
                ">=" => l >= r,
                "<=" => l <= r,
                _ => false,
            };
        }

        return op switch
        {
            "==" => string.Equals(left, right, StringComparison.Ordinal),
            "!=" => !string.Equals(left, right, StringComparison.Ordinal),
            "contains" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            "notcontains" => !left.Contains(right, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    // ═══════════════════════════════════════════════
    //  ACTION DISPATCHER  (recursive for nested IF blocks)
    // ═══════════════════════════════════════════════

    private async Task ExecuteActionsAsync(List<MacroAction> actions, CancellationToken token)
    {
        for (int idx = 0; idx < actions.Count; idx++)
        {
            token.ThrowIfCancellationRequested();

            var action = actions[idx];

            if (!action.IsEnabled)
            {
                OnLog($"  [{idx}] {action.DisplayName} — SKIPPED (disabled)");
                continue;
            }

            try
            {
                var appCfg = AppSettings.Load();
                await BehaviorRandomizer.BetweenActionsAsync(
                    _behaviorState,
                    appCfg,
                    HardwareMode,
                    _runtimeTargetHwnd,
                    p => _mouseMover.MoveToAsync(p, BezierMouseMover.ParseProfile(appCfg.MouseProfileName), token),
                    (b, v, ct) => _macroRunner.Timing.WaitAsync(b, v, ct),
                    OnLog,
                    token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                OnLog($"[Anti-Detection] ⚠ {ex.Message}");
            }

            ActionStarted?.Invoke(action, idx);

            switch (action)
            {
                case LaunchAndBindAction launch:
                    await ExecuteLaunchAndBindAsync(launch, token).ConfigureAwait(false);
                    break;

                case ClickAction click:
                    EnsureDesktopTargetBound();
                    await ExecuteClickAsync(click, token).ConfigureAwait(false);
                    break;

                case WaitAction wait:
                    await ExecuteWaitAsync(wait, token);
                    break;

                case RepeatAction repeat:
                    await ExecuteRepeatAsync(repeat, token).ConfigureAwait(false);
                    break;

                case TypeAction type:
                    EnsureDesktopTargetBound();
                    await ExecuteTypeAsync(type, token);
                    break;

                case KeyPressAction kpa:
                    EnsureDesktopTargetBound();
                    await ExecuteKeyPressAsync(kpa, token);
                    break;

                case IfImageAction ifImage:
                    EnsureDesktopTargetBound();
                    await ExecuteIfImageAsync(ifImage, token);
                    break;

                case IfTextAction ifText:
                    EnsureDesktopTargetBound();
                    await ExecuteIfTextAsync(ifText, token);
                    break;

                case WebAction webAction:
                    await ExecuteWebActionAsync(
                        ExpandRuntime(webAction.Url),
                        ExpandRuntime(webAction.Selector),
                        webAction.ActionType.ToString(),
                        ExpandRuntime(webAction.TextToType),
                        token);
                    break;

                case WebNavigateAction webNav:
                    await ExecuteWebNavigateAsync(webNav, token);
                    break;

                case WebClickAction webClick:
                    await ExecuteWebClickAsync(webClick, token);
                    break;

                case WebTypeAction webType:
                    await ExecuteWebTypeAsync(webType, token);
                    break;

                case SystemAction sys:
                    await ExecuteSystemActionAsync(sys, token).ConfigureAwait(false);
                    break;

                case SetVariableAction setVar:
                {
                    string resolved;
                    if (string.Equals(setVar.ValueSource, "Clipboard", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            resolved = WinForms.Clipboard.GetText() ?? string.Empty;
                        }
                        catch (Exception ex)
                        {
                            resolved = string.Empty;
                            OnLog($"    ⚠ Clipboard: {ex.Message}");
                        }
                    }
                    else
                    {
                        resolved = ExpandRuntime(setVar.Value);
                    }

                    string op = (setVar.Operation ?? "Set").Trim();
                    string name = setVar.VarName.Trim();
                    if (string.Equals(op, "Increment", StringComparison.OrdinalIgnoreCase))
                    {
                        int amt = int.TryParse(resolved, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ia)
                            ? ia
                            : 1;
                        _vars.Increment(name, amt);
                    }
                    else if (string.Equals(op, "Decrement", StringComparison.OrdinalIgnoreCase))
                    {
                        int amt = int.TryParse(resolved, NumberStyles.Integer, CultureInfo.InvariantCulture, out int da)
                            ? da
                            : 1;
                        _vars.Increment(name, -amt);
                    }
                    else
                    {
                        _vars.Set(name, resolved);
                    }

                    string strVal = _vars.Get(name)?.ToString() ?? _vars.GetString(name, string.Empty);
                    _variableStore.Set(name, strVal,
                        string.Equals(setVar.ValueSource, "Clipboard", StringComparison.OrdinalIgnoreCase)
                            ? "Clipboard"
                            : "Manual");

                    OnLog($"    → VAR {name} = {_vars.Get(name)} [{op}]");
                    break;
                }

                case OcrRegionAction ocrRegion:
                    await ExecuteOcrRegionAsync(ocrRegion, token).ConfigureAwait(false);
                    break;

                case ClearVariableAction clearVar:
                {
                    if (string.IsNullOrWhiteSpace(clearVar.VarName))
                    {
                        _variableStore.Clear();
                        OnLog("    → Xóa tất cả biến chuỗi {{…}} (VariableStore)");
                    }
                    else
                    {
                        string n = clearVar.VarName.Trim();
                        _variableStore.Remove(n);
                        _vars.Remove(n);
                        OnLog($"    → Xóa biến '{n}'");
                    }

                    break;
                }

                case LogVariableAction logVar:
                {
                    string n = logVar.VarName.Trim();
                    string v = _variableStore.Get(n, _vars.GetString(n, string.Empty));
                    OnLog($"    → LOG VAR: {n} = {Truncate(v, 200)}");
                    _runReport.AppendLine($"[{DateTime.Now:HH:mm:ss}] {n} = {v}");
                    break;
                }

                case IfVariableAction ifVar:
                {
                    string vn = ifVar.VarName.Trim();
                    string left = _variableStore.Get(vn, _vars.GetString(vn, string.Empty));
                    string right = ExpandRuntime(ifVar.Value);
                    string cmp = (ifVar.CompareOp ?? "==").Trim();
                    bool condResult = EvaluateCondition(left, cmp, right);
                    OnLog($"    → IF {ifVar.VarName} {cmp} {right} → {condResult}");
                    await ExecuteActionsAsync(condResult ? ifVar.ThenActions : ifVar.ElseActions, token)
                        .ConfigureAwait(false);
                    break;
                }

                case LogAction log:
                {
                    string msg = ExpandRuntime(log.Message);
                    OnLog($"    → LOG: {msg}");
                    _runReport.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
                    break;
                }

                case TryCatchAction tryCatch:
                    try
                    {
                        await ExecuteActionsAsync(tryCatch.TryActions, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        OnLog($"    → CATCH: {ex.Message} → running CatchActions");
                        _vars.Set("lastError", ex.Message);
                        await ExecuteActionsAsync(tryCatch.CatchActions, token).ConfigureAwait(false);
                    }

                    break;

                case TelegramAction tg:
                    await ExecuteTelegramAsync(tg, token).ConfigureAwait(false);
                    break;

                default:
                    OnLog($"  [{idx}] Unknown action type: {action.GetType().Name}");
                    break;
            }

            _macroRunner.Timing.NotifyMacroStepCompleted();
            ActionCompleted?.Invoke(action, idx);
        }
    }

    // ═══════════════════════════════════════════════
    //  ACTION HANDLERS
    // ═══════════════════════════════════════════════

    private async Task ExecuteOcrRegionAsync(OcrRegionAction act, CancellationToken token)
    {
        var region = new Rectangle(act.ScreenX, act.ScreenY, act.ScreenWidth, act.ScreenHeight);
        int vx = (int)System.Windows.SystemParameters.VirtualScreenLeft;
        int vy = (int)System.Windows.SystemParameters.VirtualScreenTop;
        int vw = (int)System.Windows.SystemParameters.VirtualScreenWidth;
        int vh = (int)System.Windows.SystemParameters.VirtualScreenHeight;
        var bounds = new Rectangle(vx, vy, vw, vh);
        if (!bounds.IntersectsWith(region))
        {
            OnLog($"    ⚠ OCR vùng ngoài màn hình — bỏ qua bước ({region})");
            return;
        }

        string varName = string.IsNullOrWhiteSpace(act.OutputVariableName) ? "ocr_result" : act.OutputVariableName.Trim();
        OnLog($"  OCR region {region} → {{" + varName + "}}");

        try
        {
            var (text, conf) = await _ocrService
                .ReadTextFromRegionWithConfidenceAsync(region, TimeSpan.FromSeconds(5), token)
                .ConfigureAwait(false);
            if (conf < 0.6)
                OnLog("    ⚠ Kết quả OCR có thể không chính xác (confidence < 60%)");

            _lastOcrText = text;
            _vars.Set(varName, text);
            _variableStore.Set(varName, text, "OCR");
            OnLog($"    → OCR ({Truncate(text, 80)})");
        }
        catch (OcrTimeoutException ex)
        {
            OnLog($"    ⚠ OCR: {ex.Message}");
        }
        catch (Exception ex)
        {
            OnLog($"    ⚠ OCR lỗi: {ex.Message}");
        }
    }

    private async Task ExecuteClickAsync(ClickAction click, CancellationToken token)
    {
        IntPtr hwnd = _runtimeTargetHwnd;
        var ad = AppSettings.Load();

        if (!Win32Api.IsInsideClientArea(hwnd, click.X, click.Y))
            OnLog($"  ⚠ ({click.X},{click.Y}) is outside client rect");

        if (HardwareMode)
        {
            Point screen = Win32Api.ClientPointToScreen(hwnd, click.X, click.Y);
            MouseProfile profile = BezierMouseMover.ParseProfile(ad.MouseProfileName);
            var btn = click.IsRightClick ? MouseButton.Right : MouseButton.Left;
            OnLog($"  HardwareMove+Click {btn} → screen ({screen.X},{screen.Y}) profile={profile}");

            try
            {
                await BehaviorRandomizer.MaybeScrollBeforeClickAsync(ad, HardwareMode, Random.Shared, OnLog, token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnLog($"[Anti-Detection] ⚠ Scroll-before-click: {ex.Message}");
            }

            try
            {
                if (ad.AntiDetectionEnabled
                    && BehaviorRandomizer.RollMisclick(Random.Shared, ad.AntiDetectionMisclickPercent))
                {
                    int ox = Random.Shared.Next(3, 9) * (Random.Shared.Next(2) == 0 ? -1 : 1);
                    int oy = Random.Shared.Next(3, 9) * (Random.Shared.Next(2) == 0 ? -1 : 1);
                    Point wrong = new(screen.X + ox, screen.Y + oy);
                    OnLog($"  [Anti-Detection] Misclick recovery → ({wrong.X},{wrong.Y}) then correct.");
                    await _mouseMover.MoveAndClickAsync(wrong, btn, profile, token).ConfigureAwait(false);
                    await _macroRunner.Timing.WaitAsync(Random.Shared.Next(200, 501), 80, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                OnLog($"[Anti-Detection] ⚠ Misclick sim: {ex.Message}");
            }

            await _mouseMover.MoveAndClickAsync(screen, btn, profile, token).ConfigureAwait(false);
            return;
        }

        if (click.IsRightClick)
        {
            OnLog($"  ControlRightClick({click.X}, {click.Y})");
            await Win32Api.ControlRightClickAsync(hwnd, click.X, click.Y);
        }
        else
        {
            OnLog($"  ControlClick({click.X}, {click.Y})");
            await Win32Api.ControlClickAsync(hwnd, click.X, click.Y);
        }
    }

    private async Task ExecuteWaitAsync(WaitAction wait, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(wait.WaitForOcrContains) && wait.OcrRegionWidth > 0 && wait.OcrRegionHeight > 0)
        {
            var region = new Rectangle(wait.OcrRegionX, wait.OcrRegionY, wait.OcrRegionWidth, wait.OcrRegionHeight);
            string needle = ExpandRuntime(wait.WaitForOcrContains);
            int maxWait = Math.Max(0, wait.WaitTimeoutMs);
            int poll = Math.Clamp(wait.OcrPollIntervalMs, 50, 5000);
            int elapsed = 0;
            OnLog($"  WaitForOcr contains \"{Truncate(needle, 40)}\" region={region} (timeout={maxWait}ms)");

            while (elapsed < maxWait)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var (text, conf) = await _ocrService
                        .ReadTextFromRegionWithConfidenceAsync(region, TimeSpan.FromSeconds(5), token)
                        .ConfigureAwait(false);
                    if (conf < 0.6)
                        OnLog("    ⚠ Kết quả OCR có thể không chính xác (confidence < 60%)");
                    if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        OnLog($"    → OCR khớp sau {elapsed}ms");
                        return;
                    }
                }
                catch (OcrTimeoutException ex)
                {
                    OnLog($"    ⚠ OCR timeout: {ex.Message}");
                }
                catch (Exception ex)
                {
                    OnLog($"    ⚠ WaitForOcr error: {ex.Message}");
                }

                int step = Math.Min(poll, maxWait - elapsed);
                if (step <= 0)
                    break;
                await _macroRunner.Timing.WaitAsync(step, Math.Max(5, step / 4), token).ConfigureAwait(false);
                elapsed += step;
            }

            OnLog($"    → WaitForOcr timeout ({maxWait}ms), continuing anyway");
            return;
        }

        if (!string.IsNullOrWhiteSpace(wait.WaitForImage))
        {
            EnsureDesktopTargetBound();
            IntPtr hwnd = _runtimeTargetHwnd;
            string waitImage = ExpandRuntime(wait.WaitForImage);
            const int PollMs = 500;
            int maxWait = Math.Max(0, wait.WaitTimeoutMs);
            int elapsed = 0;
            bool found = false;

            OnLog($"  WaitForImage \"{Path.GetFileName(waitImage)}\" (timeout={maxWait}ms, threshold={wait.WaitThreshold:P0})");

            do
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    Point? match = VisionEngine.FindImageOnWindowMultiScale(
                        hwnd,
                        waitImage,
                        wait.WaitThreshold,
                        scales: null,
                        searchRegion: null);
                    if (match.HasValue)
                    {
                        found = true;
                        OnLog($"    → Ảnh đã hiện sau {elapsed}ms — tiếp tục");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    OnLog($"    ⚠ WaitForImage vision error: {ex.Message}");
                    break;
                }

                if (elapsed >= maxWait)
                    break;

                int step = Math.Min(PollMs, maxWait - elapsed);
                if (step <= 0)
                    break;

                await _macroRunner.Timing.WaitAsync(step, Math.Max(10, step / 4), token).ConfigureAwait(false);
                elapsed += step;
            } while (elapsed < maxWait);

            if (!found && elapsed >= maxWait)
                OnLog($"    → WaitForImage timeout ({maxWait}ms), continuing anyway");

            return;
        }

        int min = wait.DelayMin;
        int max = wait.DelayMax;
        if (max < min)
            (min, max) = (max, min);

        min = Math.Max(0, min);
        max = Math.Max(min, max);

        int ms;
        if (min != max)
        {
            ms = Random.Shared.Next(min, max + 1);
            OnLog($"  Wait {ms}ms (random {min}-{max}ms)");
        }
        else if (min == 1000 && max == 1000 && wait.Milliseconds != 1000)
        {
            ms = wait.Milliseconds;
            OnLog($"  Wait {ms}ms");
        }
        else
        {
            ms = min;
            OnLog($"  Wait {ms}ms");
        }

        int variance = min != max ? Math.Max(1, (max - min) / 2) : Math.Max(1, ms / 4);
        await _macroRunner.Timing.WaitAsync(ms, variance, token).ConfigureAwait(false);
    }

    private async Task ExecuteRepeatAsync(RepeatAction repeat, CancellationToken token)
    {
        EnsureDesktopTargetBound();
        IntPtr hwnd = _runtimeTargetHwnd;
        string breakPath = ExpandRuntime(repeat.BreakIfImagePath);

        bool infinite = repeat.RepeatCount == 0;
        int iteration = 0;

        while (infinite || iteration < repeat.RepeatCount)
        {
            token.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(breakPath))
            {
                try
                {
                    Point? breakMatch = VisionEngine.FindImageOnWindowMultiScale(
                        hwnd,
                        breakPath,
                        repeat.BreakThreshold,
                        scales: null,
                        searchRegion: null);
                    if (breakMatch.HasValue)
                    {
                        OnLog($"    → Break condition met at iteration {iteration}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    OnLog($"    ⚠ Repeat break vision error: {ex.Message}");
                    break;
                }
            }

            OnLog($"    → Loop iteration {iteration + 1}" +
                  (infinite ? " (∞)" : $"/{repeat.RepeatCount}"));

            await ExecuteActionsAsync(repeat.LoopActions, token).ConfigureAwait(false);

            iteration++;
            if (repeat.IntervalMs > 0 && (infinite || iteration < repeat.RepeatCount))
                await _macroRunner.Timing.WaitAsync(repeat.IntervalMs, Math.Max(5, repeat.IntervalMs / 4), token)
                    .ConfigureAwait(false);
        }

        OnLog($"    → Loop finished after {iteration} iteration(s)");
    }

    private async Task ExecuteTypeAsync(TypeAction type, CancellationToken token)
    {
        IntPtr hwnd = _runtimeTargetHwnd;
        IntPtr target = Win32Api.FindInputChild(hwnd);
        bool isChild = target != hwnd;
        string suffix = isChild
            ? $" → child 0x{target:X} [{Win32Api.GetWindowClassName(target)}]"
            : "";

        string text = ExpandRuntime(type.Text);
        OnLog($"  TypeText \"{Truncate(text, 40)}\" (delay={type.KeyDelayMs}ms){suffix}");

        var ad = AppSettings.Load();
        bool scanHardware = HardwareMode && ad.AntiDetectionEnabled && ad.AntiDetectionUseScanCodeTyping;

        if (scanHardware)
        {
            foreach (char c in text)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (!InputSpoofingService.TrySendCharHardware(c))
                        Win32Api.PostMessage(target, Win32Api.WM_CHAR, (IntPtr)c, IntPtr.Zero);
                }
                catch (Exception ex)
                {
                    OnLog($"    ⚠ Scan-type fallback: {ex.Message}");
                    Win32Api.PostMessage(target, Win32Api.WM_CHAR, (IntPtr)c, IntPtr.Zero);
                }

                int baseD = type.KeyDelayMs > 0 ? type.KeyDelayMs : Random.Shared.Next(40, 121);
                await _macroRunner.Timing.WaitAsync(baseD, Math.Max(8, baseD / 2), token).ConfigureAwait(false);
            }

            return;
        }

        if (type.KeyDelayMs <= 0)
        {
            Win32Api.ControlSendText(target, text);
        }
        else
        {
            foreach (char c in text)
            {
                token.ThrowIfCancellationRequested();
                Win32Api.PostMessage(target, Win32Api.WM_CHAR, (IntPtr)c, IntPtr.Zero);
                await _macroRunner.Timing.WaitAsync(type.KeyDelayMs, Math.Max(5, type.KeyDelayMs / 2), token)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteKeyPressAsync(KeyPressAction kpa, CancellationToken token)
    {
        if (kpa.VirtualKeyCode <= 0)
        {
            OnLog("  KeyPress — SKIPPED (no key set)");
            return;
        }

        IntPtr hwnd = _runtimeTargetHwnd;
        IntPtr target = Win32Api.FindInputChild(hwnd);
        int vk = kpa.VirtualKeyCode;
        int hold = Math.Max(0, kpa.HoldDurationMs);

        // ── PATH 1: Printable character ──────────────────────────────────────
        // TryGetPrintableChar handles Shift+2 → '@', Shift+a → 'A', etc.
        // by translating through the current keyboard layout.
        char? printable = TryGetPrintableChar(vk, kpa.Modifiers.Shift, kpa.Modifiers.Ctrl, kpa.Modifiers.Alt);
        if (printable.HasValue)
        {
            Win32Api.PostMessage(target, WM_CHAR, (IntPtr)printable.Value, IntPtr.Zero);
            await Task.Delay(hold, token).ConfigureAwait(false);
            OnLog($"  KeyPress {kpa.KeyName} → WM_CHAR '{printable.Value}' (U+{((int)printable.Value):X4})");
            return;
        }

        // ── PATH 2: Ctrl+A → select all via EM_SETSEL ────────────────────────
        if (kpa.Modifiers.Ctrl && vk == 0x41)
        {
            // EM_SETSEL: wParam=start, lParam=end; (-1,-1) selects everything
            Win32Api.PostMessage(target, EM_SETSEL, IntPtr.Zero, (IntPtr)(-1));
            OnLog("  KeyPress Ctrl+A → EM_SETSEL (select all)");
            return;
        }

        // ── PATH 3: Ctrl+C/V/X/Z → semantic clipboard messages ───────────────
        // These bypass the keyboard layout entirely and go straight to the
        // edit control's built-in clipboard handler — works on any background window.
        uint? semantic = GetSemanticMessage(vk, kpa.Modifiers);
        if (semantic.HasValue)
        {
            Win32Api.PostMessage(target, semantic.Value, IntPtr.Zero, IntPtr.Zero);
            string name = semantic.Value switch
            {
                0x0301 => "WM_COPY",
                0x0302 => "WM_PASTE",
                0x0300 => "WM_CUT",
                0x0304 => "WM_UNDO",
                _ => $"0x{semantic.Value:X4}"
            };
            OnLog($"  KeyPress {kpa.KeyName} → {name}");
            return;
        }

        // ── PATH 4: Everything else ───────────────────────────────────────────
        // F1-F24, Enter, Tab, arrows, Ctrl+S, Alt+F4, etc.
        // → raw WM_KEYDOWN/WM_KEYUP with staggered timing and correct lParam.
        IntPtr downParam = BuildKeyLParam(vk, isKeyUp: false);
        IntPtr upParam   = BuildKeyLParam(vk, isKeyUp: true);

        // Press modifiers in order (with 20ms gap so message queue can drain)
        if (kpa.Modifiers.Shift)
        {
            Win32Api.PostMessage(target, Win32Api.WM_KEYDOWN, (IntPtr)0x10, BuildKeyLParam(0x10, false));
            await Task.Delay(20, token).ConfigureAwait(false);
        }
        if (kpa.Modifiers.Ctrl)
        {
            Win32Api.PostMessage(target, Win32Api.WM_KEYDOWN, (IntPtr)0x11, BuildKeyLParam(0x11, false));
            await Task.Delay(20, token).ConfigureAwait(false);
        }
        if (kpa.Modifiers.Alt)
        {
            Win32Api.PostMessage(target, 0x0104u, (IntPtr)0x12, BuildKeyLParam(0x12, false));
            await Task.Delay(20, token).ConfigureAwait(false);
        }

        // Press main key → hold → release
        Win32Api.PostMessage(target, Win32Api.WM_KEYDOWN, (IntPtr)vk, downParam);
        await Task.Delay(Math.Max(hold, 50), token).ConfigureAwait(false);
        Win32Api.PostMessage(target, Win32Api.WM_KEYUP, (IntPtr)vk, upParam);

        // Release modifiers in reverse order (with 20ms gap)
        await Task.Delay(20, token).ConfigureAwait(false);
        if (kpa.Modifiers.Alt)
        {
            Win32Api.PostMessage(target, 0x0105u, (IntPtr)0x12, BuildKeyLParam(0x12, true));
            await Task.Delay(20, token).ConfigureAwait(false);
        }
        if (kpa.Modifiers.Ctrl)
        {
            Win32Api.PostMessage(target, Win32Api.WM_KEYUP, (IntPtr)0x11, BuildKeyLParam(0x11, true));
            await Task.Delay(20, token).ConfigureAwait(false);
        }
        if (kpa.Modifiers.Shift)
        {
            Win32Api.PostMessage(target, Win32Api.WM_KEYUP, (IntPtr)0x10, BuildKeyLParam(0x10, true));
        }

        OnLog($"  KeyPress {kpa.KeyName} (VK=0x{vk:X2} SC=0x{MapVirtualKey((uint)vk, 0):X2})");
    }

    /// <summary>
    /// Maps Ctrl+key combos to semantic clipboard/edit-control messages.
    /// Returns null for non-clipboard combos (falls through to raw key path).
    /// </summary>
    private static uint? GetSemanticMessage(int vkCode, KeyModifiers mods)
    {
        if (!mods.Ctrl || mods.Alt) return null;
        return vkCode switch
        {
            0x43 => WM_COPY,  // Ctrl+C
            0x56 => WM_PASTE, // Ctrl+V
            0x58 => WM_CUT,   // Ctrl+X
            0x5A => WM_UNDO,  // Ctrl+Z
            _    => null
        };
    }

    /// <summary>
    /// Translates a VK + modifier state into a printable character using the current
    /// keyboard layout (supports Vietnamese/Unikey, Japanese, etc.). Returns null for
    /// non-printable keys (F1-F24, Enter, Tab, arrows, Ctrl/Alt combos).
    /// </summary>
    private static char? TryGetPrintableChar(int vkCode, bool shift, bool ctrl, bool alt)
    {
        // Never printable: Ctrl or Alt combos, function keys, or classic control keys
        if (ctrl || alt) return null;
        if (vkCode is >= 0x70 and <= 0x87) return null;          // F1–F24
        if (vkCode is 0x08 or 0x09 or 0x0D or 0x1B or 0x2E) return null; // BS,Tab,Enter,Esc,Del

        byte[] keyState = new byte[256];
        if (shift) keyState[0x10] = 0x80; // VK_SHIFT pressed

        var sb = new System.Text.StringBuilder(4);
        IntPtr layout = GetKeyboardLayout(0); // current thread keyboard layout
        int result = ToUnicodeEx((uint)vkCode, 0, keyState, sb, sb.Capacity, 0, layout);

        if (result == 1 && sb.Length > 0 && !char.IsControl(sb[0]))
            return sb[0];

        return null;
    }

    /// <summary>
    /// Builds the correct lParam for WM_KEYDOWN / WM_KEYUP messages.
    /// Bit layout: repeat-count(0-15) | scan-code(16-23) | extended-flag(24) | reserved(25-28) | transition(30-31).
    /// This ensures games and emulators that read lParam directly receive a valid scan code.
    /// </summary>
    private static IntPtr BuildKeyLParam(int virtualKeyCode, bool isKeyUp)
    {
        uint scanCode = MapVirtualKey((uint)virtualKeyCode, 0); // MAPVK_VK_TO_VSC

        // Extended keys: right-side modifiers, arrows, Insert/Delete, Home/End, PgUp/PgDn
        bool isExtended = virtualKeyCode is
            0x21 or 0x22 or 0x23 or 0x24 or // PageUp, PageDown, End, Home
            0x25 or 0x26 or 0x27 or 0x28 or // Arrow keys
            0x2D or 0x2E or                   // Insert, Delete
            0xA1 or 0xA3 or 0xA5;            // Right Ctrl, Right Shift, Right Alt

        uint lParam = 1;                             // repeat count = 1
        lParam |= (scanCode & 0xFF) << 16;          // scan code in bits 16-23
        if (isExtended) lParam |= 0x01000000;        // extended-key bit
        if (isKeyUp)    lParam |= 0xC0000000;        // bits 30-31 = key-up transition

        return (IntPtr)lParam;
    }

    private async Task ExecuteIfImageAsync(IfImageAction ifImage, CancellationToken token)
    {
        IntPtr hwnd = _runtimeTargetHwnd;
        string imagePath = ExpandRuntime(ifImage.ImagePath);
        const int PollMs = 500;
        int maxWait = Math.Max(0, ifImage.TimeoutMs);
        int elapsed = 0;
        Point? match = null;

        OnLog($"  IfImageFound \"{Path.GetFileName(imagePath)}\" " +
              $"(threshold={ifImage.Threshold:P0}, timeout={maxWait}ms)");

        do
        {
            token.ThrowIfCancellationRequested();

            try
            {
                match = VisionEngine.FindImageOnWindowMultiScale(
                    hwnd,
                    imagePath,
                    ifImage.Threshold,
                    scales: null,
                    searchRegion: ifImage.SearchRegion);
            }
            catch (Exception ex)
            {
                OnLog($"    ⚠ Vision error: {ex.Message}");
                break;
            }

            if (match.HasValue)
                break;

            if (elapsed >= maxWait)
                break;

            int wait = Math.Min(PollMs, maxWait - elapsed);
            if (wait <= 0)
                break;

            await _macroRunner.Timing.WaitAsync(wait, Math.Max(10, wait / 4), token).ConfigureAwait(false);
            elapsed += wait;
        } while (elapsed < maxWait);

        if (match.HasValue)
        {
            OnLog($"    → FOUND at ({match.Value.X}, {match.Value.Y}) after {elapsed}ms");
            try
            {
                var det = VisionEngine.FindImageOnWindowDetailed(hwnd, imagePath, ifImage.SearchRegion);
                if (det.HasValue)
                    _lastImageMatchConfidence = det.Value.Confidence;
            }
            catch
            {
                _lastImageMatchConfidence = 0;
            }

            if (ifImage.ClickOnFound)
            {
                int off = Math.Clamp(ifImage.RandomOffset, 0, 64);
                if (HardwareMode)
                {
                    int ox = Random.Shared.Next(-off, off + 1);
                    int oy = Random.Shared.Next(-off, off + 1);
                    int cx = match.Value.X + ox;
                    int cy = match.Value.Y + oy;
                    Point screen = Win32Api.ClientPointToScreen(hwnd, cx, cy);
                    MouseProfile profile = BezierMouseMover.ParseProfile(AppSettings.Load().MouseProfileName);
                    OnLog($"    → Hardware click at client ({cx},{cy}) screen ({screen.X},{screen.Y}) profile={profile}");
                    await _mouseMover.MoveAndClickAsync(screen, MouseButton.Left, profile, token).ConfigureAwait(false);
                }
                else
                {
                    await Win32Api.StealthClickOnFoundImage(hwnd, match.Value, randomOffsetRange: off, token);
                    OnLog($"    → StealthClick at ({match.Value.X},{match.Value.Y}) [offset ±{off}px]");
                }
            }

            if (ifImage.ThenActions.Count > 0)
            {
                OnLog($"    → Executing {ifImage.ThenActions.Count} THEN action(s)");
                await ExecuteActionsAsync(ifImage.ThenActions, token);
            }
        }
        else
        {
            OnLog($"    → NOT FOUND after {maxWait}ms timeout → running ElseActions");

            if (ifImage.ElseActions.Count > 0)
            {
                OnLog($"    → Executing {ifImage.ElseActions.Count} ELSE action(s)");
                await ExecuteActionsAsync(ifImage.ElseActions, token);
            }
        }
    }

    private async Task ExecuteWebNavigateAsync(WebNavigateAction nav, CancellationToken token)
    {
        string url = ExpandRuntime(nav.Url);
        if (string.IsNullOrWhiteSpace(url))
        {
            OnLog("  WebNavigate — SKIPPED (empty URL)");
            return;
        }

        _playwrightEngine ??= new PlaywrightEngine
        {
            Mode = BrowserMode,
            AdsPowerProfileId = _currentProfileId,
        };
        OnLog($"  WebNavigate: {url}");
        await _playwrightEngine.MapsAsync(url.Trim(), token).ConfigureAwait(false);
    }

    private async Task ExecuteWebClickAsync(WebClickAction click, CancellationToken token)
    {
        string sel = ExpandRuntime(click.CssSelector);
        if (string.IsNullOrWhiteSpace(sel))
        {
            OnLog("  WebClick — SKIPPED (empty selector)");
            return;
        }

        _playwrightEngine ??= new PlaywrightEngine
        {
            Mode = BrowserMode,
            AdsPowerProfileId = _currentProfileId,
        };
        await _playwrightEngine.EnsureBrowserStartedAsync(token).ConfigureAwait(false);
        OnLog($"  WebClick: {sel}");
        await _playwrightEngine.ClickSelectorAsync(sel.Trim(), token).ConfigureAwait(false);
    }

    private async Task ExecuteWebTypeAsync(WebTypeAction type, CancellationToken token)
    {
        string sel = ExpandRuntime(type.CssSelector);
        if (string.IsNullOrWhiteSpace(sel))
        {
            OnLog("  WebType — SKIPPED (empty selector)");
            return;
        }

        string typed = ExpandRuntime(type.TextToType ?? "");
        _playwrightEngine ??= new PlaywrightEngine
        {
            Mode = BrowserMode,
            AdsPowerProfileId = _currentProfileId,
        };
        await _playwrightEngine.EnsureBrowserStartedAsync(token).ConfigureAwait(false);
        OnLog($"  WebType: {sel} ← \"{Truncate(typed, 40)}\"");
        await _playwrightEngine.TypeSelectorAsync(sel.Trim(), typed, token)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Unified handler for the new WebAction type. Dispatches to the correct
    /// Playwright operation based on <paramref name="actionType"/>.
    /// Runs Playwright initialization on a background thread to prevent UI freezing.
    /// </summary>
    public async Task ExecuteWebActionAsync(
        string url, string selector, string actionType, string textToType, CancellationToken token)
    {
        _playwrightEngine ??= new PlaywrightEngine
        {
            Mode = BrowserMode,
            AdsPowerProfileId = _currentProfileId,
        };
        await _playwrightEngine.EnsureBrowserStartedAsync(token).ConfigureAwait(false);

        switch (actionType)
        {
            case "Navigate":
                if (string.IsNullOrWhiteSpace(url))
                {
                    OnLog("  WebAction(Navigate) — SKIPPED (empty URL)");
                    return;
                }
                OnLog($"  WebAction Navigate: {url}");
                await _playwrightEngine.MapsAsync(url.Trim(), token).ConfigureAwait(false);
                break;

            case "Click":
                if (string.IsNullOrWhiteSpace(selector))
                {
                    OnLog("  WebAction(Click) — SKIPPED (empty selector)");
                    return;
                }
                OnLog($"  WebAction Click: {selector}");
                await _playwrightEngine.ClickSelectorAsync(selector.Trim(), token).ConfigureAwait(false);
                break;

            case "Type":
                if (string.IsNullOrWhiteSpace(selector))
                {
                    OnLog("  WebAction(Type) — SKIPPED (empty selector)");
                    return;
                }
                OnLog($"  WebAction Type: {selector} ← \"{Truncate(textToType, 40)}\"");
                await _playwrightEngine.TypeSelectorAsync(selector.Trim(), textToType ?? "", token)
                    .ConfigureAwait(false);
                break;

            case "Scrape":
                if (string.IsNullOrWhiteSpace(selector))
                {
                    OnLog("  WebAction(Scrape) — SKIPPED (empty selector)");
                    return;
                }
                OnLog($"  WebAction Scrape: {selector}");
                string scraped = await _playwrightEngine.ScrapeSelectorAsync(selector.Trim(), token)
                    .ConfigureAwait(false);
                OnLog($"    → Scraped {scraped.Length} chars: \"{Truncate(scraped, 80)}\"");
                break;

            default:
                OnLog($"  WebAction — unknown type: {actionType}");
                break;
        }
    }

    private async Task ExecuteIfTextAsync(IfTextAction ifText, CancellationToken token)
    {
        IntPtr hwnd = _runtimeTargetHwnd;
        string needle = ExpandRuntime(ifText.Text);
        OnLog($"  IfTextFound \"{Truncate(needle, 30)}\" " +
              $"(ignoreCase={ifText.IgnoreCase}, partial={ifText.PartialMatch})");

        string ocrResult;
        try
        {
            ocrResult = VisionEngine.ExtractTextFromWindow(hwnd);
        }
        catch (Exception ex)
        {
            OnLog($"    ⚠ OCR error: {ex.Message}");
            ocrResult = string.Empty;
        }

        bool found;
        if (ifText.PartialMatch)
        {
            found = ocrResult.Contains(
                needle,
                ifText.IgnoreCase
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        else
        {
            found = string.Equals(
                ocrResult.Trim(), needle.Trim(),
                ifText.IgnoreCase
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }

        if (found)
        {
            OnLog("    → TEXT FOUND");

            if (ifText.ThenActions.Count > 0)
            {
                OnLog($"    → Executing {ifText.ThenActions.Count} THEN action(s)");
                await ExecuteActionsAsync(ifText.ThenActions, token);
            }
        }
        else
        {
            OnLog("    → TEXT NOT FOUND");

            if (ifText.ElseActions.Count > 0)
            {
                OnLog($"    → Executing {ifText.ElseActions.Count} ELSE action(s)");
                await ExecuteActionsAsync(ifText.ElseActions, token);
            }
        }
    }

    private async Task ExecuteLaunchAndBindAsync(LaunchAndBindAction launch, CancellationToken token)
    {
        string urlRaw = ExpandRuntime(launch.Url);
        if (string.IsNullOrWhiteSpace(urlRaw))
        {
            OnLog("  Launch & Bind — SKIPPED (empty URL)");
            return;
        }

        string url = urlRaw.Trim();
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;

        string exe = launch.Browser == LaunchBrowserKind.Edge
            ? ResolveEdgeExecutable()
            : ResolveChromeExecutable();

        OnLog($"  Launch & Bind: {launch.Browser} → {url}");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(url);

        using var started = Process.Start(psi);
        if (started is null)
            throw new InvalidOperationException("Process.Start returned null for browser launch.");

        int rootPid = started.Id;
        int timeoutMs = launch.BindTimeoutMs > 1000 ? launch.BindTimeoutMs : 60_000;
        int pollMs = Math.Clamp(launch.PollIntervalMs, 100, 2000);
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);

        IntPtr found = IntPtr.Zero;
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            await _macroRunner.Timing.WaitAsync(pollMs, Math.Max(15, pollMs / 4), token).ConfigureAwait(false);

            try
            {
                using var proc = Process.GetProcessById(rootPid);
                proc.Refresh();
                IntPtr mw = proc.MainWindowHandle;
                if (mw != IntPtr.Zero && Win32Api.IsWindow(mw)
                    && !string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                {
                    found = mw;
                    break;
                }
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException(
                    "Launch & Bind failed: the browser process exited before a main window appeared.");
            }
            catch
            {
                // Transient errors (e.g. access denied) — keep polling until timeout.
            }
        }

        if (found == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Launch & Bind timed out after {timeoutMs} ms — the started process did not report a main window. " +
                "Try a longer timeout or pick the browser window manually in the target list.");
        }

        _runtimeTargetHwnd = found;
        OnLog($"[Auto-Bind] Successfully bound to new window. New HWND: 0x{_runtimeTargetHwnd:X}");
        TargetWindowRebound?.Invoke(_runtimeTargetHwnd);
    }

    private static string ResolveChromeExecutable()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
        ];
        foreach (string c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        throw new FileNotFoundException("Google Chrome not found. Install Chrome or use Edge in Launch & Bind.");
    }

    private static string ResolveEdgeExecutable()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
        ];
        foreach (string c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        throw new FileNotFoundException("Microsoft Edge not found. Install Edge or use Chrome in Launch & Bind.");
    }

    // ═══════════════════════════════════════════════
    //  TELEGRAM
    // ═══════════════════════════════════════════════

    private Task ExecuteTelegramAsync(TelegramAction tg, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(tg.BotToken) || string.IsNullOrWhiteSpace(tg.ChatId))
        {
            OnLog("  Telegram — SKIPPED (BotToken hoặc ChatId trống)");
            return Task.CompletedTask;
        }

        string rawMessage = tg.Message ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            OnLog("  Telegram — SKIPPED (message trống)");
            return Task.CompletedTask;
        }

        string resolved = VariableResolver.Resolve(rawMessage, _runtimeVariables);
        string resolvedToken = VariableResolver.Resolve(tg.BotToken, _runtimeVariables);
        string resolvedChatId = VariableResolver.Resolve(tg.ChatId, _runtimeVariables);

        OnLog($"  Telegram → \"{Truncate(resolved, 40)}\" @ {Truncate(resolvedChatId, 20)}");

        _ = Task.Run(async () =>
        {
            try
            {
                await TelegramService.SendAsync(resolvedToken, resolvedChatId, resolved, OnLog)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnLog($"  ⚠ Telegram error: {ex.Message}");
            }
        });

        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════
    //  TELEGRAM COMPLETION (fire-and-forget)
    // ═══════════════════════════════════════════════

    private void FireTelegramCompletion(
        MacroScript script,
        int rowsDone,
        int total,
        bool hasError,
        string? lastErrorMessage)
    {
        if (!script.SendTelegramOnComplete)
            return;

        string token = !string.IsNullOrWhiteSpace(script.TelegramBotToken)
            ? script.TelegramBotToken
            : AppSettings.Load().TelegramBotToken;

        string chatId = !string.IsNullOrWhiteSpace(script.TelegramChatId)
            ? script.TelegramChatId
            : AppSettings.Load().TelegramChatId;

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                string duration = (DateTime.Now - _sessionStartTime).ToString(@"hh\:mm\:ss");
                string template = hasError
                    ? script.TelegramErrorMessage
                    : script.TelegramCompleteMessage;

                string msg = template
                    .Replace("{MacroName}", script.Name ?? "Macro")
                    .Replace("{RowsDone}", rowsDone.ToString())
                    .Replace("{RowsTotal}", total.ToString())
                    .Replace("{Duration}", duration)
                    .Replace("{MachineName}", Environment.MachineName)
                    .Replace("{ErrorMessage}",
                        string.IsNullOrEmpty(lastErrorMessage)
                            ? "Unknown error"
                            : System.Net.WebUtility.HtmlEncode(lastErrorMessage));

                await TelegramService.SendAsync(token, chatId, msg, Log)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[Telegram] Gửi thông báo thất bại: {ex.Message}");
            }
        });
    }

    // ═══════════════════════════════════════════════
    //  UTIL
    // ═══════════════════════════════════════════════

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "…");
}
