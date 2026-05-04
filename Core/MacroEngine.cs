using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using WinForms = System.Windows.Forms;
using WpfApp = System.Windows.Application;
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
    // Guards: Clipboard, SetForegroundWindow, SendInput physical mouse, keybd_event
    // Prevents cross-window input contamination when multiple macros target different Discord windows
    private static readonly SemaphoreSlim _osResourceLock = new(1, 1);

    // Pinned at construction — never re-derived during execution (FIX 2)
    private IntPtr _targetHwnd;
    private uint _targetThread;
    private uint _targetPid;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private void InitializeTargetHwnd(IntPtr hwnd)
    {
        _targetHwnd = hwnd;
        _targetThread = GetWindowThreadProcessId(hwnd, out uint pid);
        _targetPid = pid;
    }

    private bool IsTargetValid()
    {
        if (!IsWindow(_targetHwnd))
        {
            OnLog($"[ERROR] Window 0x{_targetHwnd:X} no longer exists — stopping macro");
            return false;
        }
        GetWindowThreadProcessId(_targetHwnd, out uint pid);
        if (pid != _targetPid)
        {
            OnLog($"[ERROR] Window PID changed: expected {_targetPid}, got {pid}");
            return false;
        }
        return true;
    }

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

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const uint WM_KEYDOWN    = 0x0100;
    private const uint WM_KEYUP      = 0x0101;
    private const uint WM_CHAR      = 0x0102;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP  = 0x0105;
    private const uint WM_CUT       = 0x0300;
    private const uint WM_COPY      = 0x0301;
    private const uint WM_PASTE     = 0x0302;
    private const uint WM_UNDO      = 0x0304;
    private const uint EM_SETSEL    = 0x00B1;

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
    /// Exposes the VariableStore for external callers (e.g. sub-macro variable passing).
    /// </summary>
    public VariableStore Variables => _variableStore;

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

    /// <summary>Maximum nesting depth for sub-macros to prevent infinite recursion.</summary>
    private const int MaxSubMacroDepth = 10;

    /// <summary>Current nesting depth of sub-macro execution.</summary>
    private readonly int _subMacroDepth;

    /// <summary>The script currently running in this engine instance (for self-call detection).</summary>
    private MacroScript? _currentScript;

    /// <summary>Run history service for recording macro execution results.</summary>
    private readonly RunHistoryService _runHistoryService = new();

    /// <summary>Current run record being populated during execution.</summary>
    private MacroRunRecord? _currentRunRecord;

    /// <summary>Creates an engine instance and wires Bézier mouse diagnostics to <see cref="Log"/>.</summary>
    public MacroEngine()
    {
        _mouseMover.DiagnosticLog = msg => Log?.Invoke(msg);
    }

    /// <summary>
    /// Creates an engine instance with an existing script and target HWND.
    /// Used by sub-macros that need to pass runtime variables.
    /// </summary>
    public MacroEngine(MacroScript script, IntPtr hwnd, Action<string>? log)
    {
        _mouseMover.DiagnosticLog = msg => log?.Invoke(msg);
        Log = log;
        TargetHwnd = hwnd;
        if (hwnd != IntPtr.Zero) InitializeTargetHwnd(hwnd);
        InitialScript = script;
        _currentScript = script;
    }

    /// <summary>
    /// Internal constructor used by <see cref="ExecuteCallMacroAsync"/> to track nesting depth.
    /// </summary>
    private MacroEngine(MacroEngine parent, MacroScript script, IntPtr hwnd, Action<string>? log)
    {
        _mouseMover.DiagnosticLog = msg => log?.Invoke(msg);
        Log = log;
        TargetHwnd = hwnd;
        if (hwnd != IntPtr.Zero) InitializeTargetHwnd(hwnd);
        InitialScript = script;
        _currentScript = script;
        _subMacroDepth = parent._subMacroDepth + 1;
    }

    /// <summary>
    /// Target Win32 window HWND for desktop automation.
    /// </summary>
    public IntPtr TargetHwnd { get; set; }

    /// <summary>
    /// Initial script to run (used with <see cref="TargetHwnd"/> constructor overload).
    /// </summary>
    public MacroScript? InitialScript { get; set; }

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
    public event Action<IReadOnlyList<(string Name, string Value, string Source)>>? VariablesUpdated;

    private void OnLog(string message) => Log?.Invoke(message);

    /// <summary>Fires the VariablesUpdated event with current live variable snapshot.</summary>
    private void FireVariablesUpdated()
    {
        var rows = GetLiveVariableRows();
        VariablesUpdated?.Invoke(rows);
    }

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

        // Validate window handle — fail fast with a clear message instead of silent crash
        if (hwnd != IntPtr.Zero && !Win32Api.IsWindow(hwnd))
        {
            OnLog($"❌ {LanguageManager.GetString("ui_Engine_TargetWindowGone")}");
            return;
        }

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
            InitializeTargetHwnd(hwnd); // FIX 2: pin HWND at execution start
            string windowTitle = Win32Api.GetWindowTitle(hwnd);
            OnLog(string.Format(LanguageManager.GetString("ui_Engine_TargetAcquired"), windowTitle, hwnd));
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

        // Create run history record
        _currentRunRecord = new MacroRunRecord
        {
            MacroName   = script.Name,
            MacroFile   = script.FilePath ?? "",
            StartTime   = DateTime.Now,
            TotalSteps  = script.Actions.Count
        };

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

                // Save successful run to history
                if (_currentRunRecord != null)
                {
                    _currentRunRecord.EndTime = DateTime.Now;
                    _currentRunRecord.Success = true;
                    _currentRunRecord.CompletedSteps = _currentRunRecord.TotalSteps;
                    _runHistoryService.Save(_currentRunRecord);
                    NotificationService.Instance.PushSuccess(
                        LanguageManager.GetString("ui_Engine_MacroComplete"),
                        string.Format(LanguageManager.GetString("ui_Engine_MacroSuccess"), script.Name),
                        script.Name);
                }
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
                OnLog($"[CRITICAL] Execution faulted: {ex.Message}");
                OnLog($"[StackTrace] {ex.StackTrace}");
                ExecutionFaulted?.Invoke(ex);
                FireTelegramCompletion(script, rowsDone: _totalRowsDone, total: _totalRowsTotal, hasError: true, lastErrorMessage: ex.Message);

                // Save failed run to history
                if (_currentRunRecord != null)
                {
                    _currentRunRecord.EndTime = DateTime.Now;
                    _currentRunRecord.Success = false;
                    _currentRunRecord.ErrorMessage = ex.Message;
                    _currentRunRecord.CompletedSteps = _totalRowsDone;
                    _runHistoryService.Save(_currentRunRecord);
                    NotificationService.Instance.PushError(
                        LanguageManager.GetString("ui_Engine_MacroFailed"),
                        string.Format(LanguageManager.GetString("ui_Engine_MacroError"), script.Name, Truncate(ex.Message, 80)),
                        script.Name);
                }

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
            OnLog($"    ⚠ {string.Format(LanguageManager.GetString("ui_Engine_VarNotSet"), key)}");
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
                throw new InvalidOperationException(LanguageManager.GetString("ui_Engine_TargetWindowClosed"));

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
                throw new InvalidOperationException(LanguageManager.GetString("ui_Engine_TargetWindowClosed"));

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
            FireVariablesUpdated();

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
                // Anti-detection behavior
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
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { OnLog($"[Anti-Detection] ⚠ {ex.Message}"); }

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
                            try { resolved = WinForms.Clipboard.GetText() ?? string.Empty; }
                            catch (Exception ex) { resolved = string.Empty; OnLog($"    ⚠ Clipboard: {ex.Message}"); }
                        }
                        else { resolved = ExpandRuntime(setVar.Value); }

                        string op = (setVar.Operation ?? "Set").Trim();
                        string name = setVar.VarName.Trim();
                        if (string.Equals(op, "Increment", StringComparison.OrdinalIgnoreCase))
                            _vars.Increment(name, int.TryParse(resolved, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ia) ? ia : 1);
                        else if (string.Equals(op, "Decrement", StringComparison.OrdinalIgnoreCase))
                            _vars.Increment(name, int.TryParse(resolved, NumberStyles.Integer, CultureInfo.InvariantCulture, out int da) ? -da : -1);
                        else
                            _vars.Set(name, resolved);

                        string strVal = _vars.Get(name)?.ToString() ?? _vars.GetString(name, string.Empty);
                        _variableStore.Set(name, strVal, string.Equals(setVar.ValueSource, "Clipboard", StringComparison.OrdinalIgnoreCase) ? "Clipboard" : "Manual");
                        OnLog($"    → VAR {name} = {_vars.Get(name)} [{op}]");
                        FireVariablesUpdated();
                        break;
                    }

                    case OcrRegionAction ocrRegion:
                        await ExecuteOcrRegionAsync(ocrRegion, token).ConfigureAwait(false);
                        break;

                    case ClearVariableAction clearVar:
                    {
                        if (string.IsNullOrWhiteSpace(clearVar.VarName))
                        { _variableStore.Clear(); OnLog(LanguageManager.GetString("ui_Engine_ClearAllVars")); }
                        else
                        { string n = clearVar.VarName.Trim(); _variableStore.Remove(n); _vars.Remove(n); OnLog(string.Format(LanguageManager.GetString("ui_Engine_ClearVar"), n)); }
                        break;
                    }

                    case ResetVariablesAction:
                    {
                        _vars.Clear();
                        _variableStore.Clear();
                        OnLog(LanguageManager.GetString("ui_Engine_ResetAllVars"));
                        FireVariablesUpdated();
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
                        await ExecuteActionsAsync(condResult ? ifVar.ThenActions : ifVar.ElseActions, token).ConfigureAwait(false);
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
                    {
                        try { await ExecuteActionsAsync(tryCatch.TryActions, token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        { OnLog($"    → CATCH: {ex.Message} → running CatchActions"); _vars.Set("lastError", ex.Message); await ExecuteActionsAsync(tryCatch.CatchActions, token).ConfigureAwait(false); }
                        break;
                    }

                    case TelegramAction tg:
                        await ExecuteTelegramAsync(tg, token).ConfigureAwait(false);
                        break;

                    case CallMacroAction cma:
                        await ExecuteCallMacroAsync(cma, token).ConfigureAwait(false);
                        break;

                    default:
                        OnLog($"  [{idx}] Unknown action type: {action.GetType().Name}");
                        break;
                }

                _macroRunner.Timing.NotifyMacroStepCompleted();
                ActionCompleted?.Invoke(action, idx);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                int currentIdx = idx;
                string actionName = action.DisplayName;
                string errMsg = $"[{currentIdx}] {actionName}: {ex.Message}";
                OnLog($"  ❌ {errMsg}");

                if (AppSettings.Instance.ScreenshotOnError)
                {
                    string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "errors");
                    string? screenshotPath = ScreenshotHelper.CaptureWindow(_runtimeTargetHwnd, folder);

                    if (screenshotPath != null)
                    {
                        string fileName = Path.GetFileName(screenshotPath);
                        OnLog($"    📸 Screenshot saved: {fileName}");

                        // Capture screenshot path in run record
                        if (_currentRunRecord != null)
                            _currentRunRecord.ScreenshotPath = screenshotPath;

                        if (AppSettings.Instance.HasTelegramToken)
                        {
                            string caption = $"❌ <b>{_currentScript?.Name ?? "Macro"}</b>\n" +
                                $"{string.Format(LanguageManager.GetString("ui_Engine_TelegramCaptionStep"), currentIdx + 1, actionName)}\n" +
                                $"{string.Format(LanguageManager.GetString("ui_Engine_TelegramCaptionError"), $"<code>{Truncate(ex.Message, 100)}</code>")}\n" +
                                $"🕐 {DateTime.Now:HH:mm:ss dd/MM/yyyy}";

                            _ = Task.Run(async () =>
                            {
                                await TelegramService.SendPhotoAsync(
                                    AppSettings.Instance.TelegramBotToken,
                                    AppSettings.Instance.TelegramChatId,
                                    screenshotPath,
                                    caption,
                                    onLog: msg => Log?.Invoke($"    {msg}"));
                            });
                        }
                    }
                    else { OnLog("    📸 Screenshot failed (window not accessible)"); }
                }

                OnLog("    → Continuing (ContinueOnError = true)");
            }
        }
    }

    // ═══════════════════════════════════════════════
    //  ACTION HANDLERS
    // ═══════════════════════════════════════════════

    private async Task ExecuteOcrRegionAsync(OcrRegionAction act, CancellationToken token)
    {
        // FIX 5: HWND liveness check (OCR region can be independent of window)
        if (_runtimeTargetHwnd != IntPtr.Zero && !IsTargetValid()) return;

        var region = new Rectangle(act.ScreenX, act.ScreenY, act.ScreenWidth, act.ScreenHeight);
        int vx = (int)System.Windows.SystemParameters.VirtualScreenLeft;
        int vy = (int)System.Windows.SystemParameters.VirtualScreenTop;
        int vw = (int)System.Windows.SystemParameters.VirtualScreenWidth;
        int vh = (int)System.Windows.SystemParameters.VirtualScreenHeight;
        var bounds = new Rectangle(vx, vy, vw, vh);
        if (!bounds.IntersectsWith(region))
        {
            OnLog($"    ⚠ {string.Format(LanguageManager.GetString("ui_Engine_OcrRegionOutOfBounds"), region)}");
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
                OnLog(LanguageManager.GetString("ui_Engine_OcrLowConfidence"));

            _lastOcrText = text;
            _vars.Set(varName, text);
            _variableStore.Set(varName, text, "OCR");
            OnLog($"    → OCR ({Truncate(text, 80)})");
            FireVariablesUpdated();
        }
        catch (OcrTimeoutException ex)
        {
            OnLog($"    ⚠ OCR: {ex.Message}");
        }
        catch (Exception ex)
        {
            OnLog($"    ⚠ {string.Format(LanguageManager.GetString("ui_Engine_OcrError"), ex.Message)}");
        }
    }

    private async Task ExecuteClickAsync(ClickAction click, CancellationToken token)
    {
        IntPtr hwnd = _runtimeTargetHwnd;

        // FIX 5: HWND liveness check
        if (!IsTargetValid()) return;

        if (!Win32Api.IsInsideClientArea(hwnd, click.X, click.Y))
            OnLog($"  ⚠ ({click.X},{click.Y}) is outside client rect");

        // ── HARDWARE MODE: SetCursorPos + mouse_event + SetForegroundWindow ──────────
        if (click.Mode == ClickMode.Hardware)
        {
            if (!await _osResourceLock.WaitAsync(TimeSpan.FromSeconds(5), token))
            {
                OnLog("[WARN] OS resource lock timeout on click — skipping");
                return;
            }
            try
            {
                var pt = new POINT { X = click.X, Y = click.Y };
                ClientToScreen(hwnd, ref pt);

                IntPtr prevFg = GetForegroundWindow();
                if (prevFg != hwnd) { SetForegroundWindow(hwnd); await Task.Delay(50, token); }

                SetCursorPos(pt.X, pt.Y);
                await Task.Delay(20, token);
                uint hwDown = click.Button switch
                {
                    MouseButton.Right => MOUSEEVENTF_RIGHTDOWN,
                    MouseButton.Middle => MOUSEEVENTF_MIDDLEDOWN,
                    _ => MOUSEEVENTF_LEFTDOWN,
                };
                uint hwUp = click.Button switch
                {
                    MouseButton.Right => MOUSEEVENTF_RIGHTUP,
                    MouseButton.Middle => MOUSEEVENTF_MIDDLEUP,
                    _ => MOUSEEVENTF_LEFTUP,
                };
                mouse_event(hwDown, 0, 0, 0, UIntPtr.Zero);
                await Task.Delay(30, token);
                mouse_event(hwUp, 0, 0, 0, UIntPtr.Zero);

                if (prevFg != IntPtr.Zero && prevFg != hwnd) SetForegroundWindow(prevFg);
                Log?.Invoke($"[Click/Hardware] ({click.X},{click.Y}) → screen ({pt.X},{pt.Y})");
            }
            finally { _osResourceLock.Release(); }
            return;
        }

        // ── RAW MODE: SendInput with absolute coordinates — hijacks physical cursor ───
        if (click.Mode == ClickMode.Raw)
        {
            if (!await _osResourceLock.WaitAsync(TimeSpan.FromSeconds(5), token))
            {
                OnLog("[WARN] OS resource lock timeout on click — skipping");
                return;
            }
            try
            {
                var pt = new POINT { X = click.X, Y = click.Y };
                ClientToScreen(hwnd, ref pt);

                int screenW = GetSystemMetrics(0);
                int screenH = GetSystemMetrics(1);
                int absX = (int)((pt.X + 0.5) * 65536.0 / screenW);
                int absY = (int)((pt.Y + 0.5) * 65536.0 / screenH);

                uint rawDown = click.Button switch
                {
                    MouseButton.Right => MOUSEEVENTF_RIGHTDOWN,
                    MouseButton.Middle => MOUSEEVENTF_MIDDLEDOWN,
                    _ => MOUSEEVENTF_LEFTDOWN,
                };
                uint rawUp = click.Button switch
                {
                    MouseButton.Right => MOUSEEVENTF_RIGHTUP,
                    MouseButton.Middle => MOUSEEVENTF_MIDDLEUP,
                    _ => MOUSEEVENTF_LEFTUP,
                };
                var inputs = new[]
                {
                    new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dx = absX, dy = absY, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESKTOP } } },
                    new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = rawDown } } },
                    new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = rawUp } } }
                };
                SendInput(3, inputs, Marshal.SizeOf<INPUT>());
                Log?.Invoke($"[Click/Raw] ({click.X},{click.Y})");
            }
            finally { _osResourceLock.Release(); }
            return;
        }

        // ── DRIVER LEVEL MODE: Interception kernel driver — bypasses anti-cheat ────
        if (click.Mode == ClickMode.DriverLevel)
        {
            if (!await _osResourceLock.WaitAsync(TimeSpan.FromSeconds(5), token))
            {
                OnLog("[WARN] OS resource lock timeout on click/Driver — skipping");
                return;
            }
            try
            {
                var pt = new POINT { X = click.X, Y = click.Y };
                ClientToScreen(hwnd, ref pt);

                if (!InterceptionService.Instance.IsInitialized)
                {
                    OnLog(string.Format(LanguageManager.GetString("ui_Engine_InterceptionNotInstalled"), "Raw mode"));
                    // Fallback to Raw
                    int screenW = GetSystemMetrics(0);
                    int screenH = GetSystemMetrics(1);
                    int absX = (int)((pt.X + 0.5) * 65536.0 / screenW);
                    int absY = (int)((pt.Y + 0.5) * 65536.0 / screenH);
                    uint drvDown = click.Button switch
                    {
                        MouseButton.Right => MOUSEEVENTF_RIGHTDOWN,
                        MouseButton.Middle => MOUSEEVENTF_MIDDLEDOWN,
                        _ => MOUSEEVENTF_LEFTDOWN,
                    };
                    uint drvUp = click.Button switch
                    {
                        MouseButton.Right => MOUSEEVENTF_RIGHTUP,
                        MouseButton.Middle => MOUSEEVENTF_MIDDLEUP,
                        _ => MOUSEEVENTF_LEFTUP,
                    };
                    var inputs = new[]
                    {
                        new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dx = absX, dy = absY, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESKTOP } } },
                        new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = drvDown } } },
                        new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = drvUp } } }
                    };
                    SendInput(3, inputs, Marshal.SizeOf<INPUT>());
                }
                else
                {
                    InterceptionService.Instance.SendMouseClick(pt.X, pt.Y, 50, click.Button);
                }

                Log?.Invoke($"[Click/Driver] ({click.X},{click.Y}) → screen ({pt.X},{pt.Y})");
            }
            finally { _osResourceLock.Release(); }
            return;
        }

        // ── STEALTH MODE (default): PostMessage — no cursor hijack, runs in background ─
        IntPtr lParam = Win32Api.MakeLParam(click.X, click.Y);
        Win32Api.PostMessage(hwnd, Win32Api.WM_MOUSEMOVE, IntPtr.Zero, lParam);
        await Task.Delay(20, token);
        if (click.Button == MouseButton.Right)
        {
            Win32Api.PostMessage(hwnd, Win32Api.WM_RBUTTONDOWN, (IntPtr)Win32Api.MK_RBUTTON, lParam);
            await Task.Delay(50, token);
            Win32Api.PostMessage(hwnd, Win32Api.WM_RBUTTONUP, IntPtr.Zero, lParam);
            Log?.Invoke($"[Click/Stealth] ({click.X},{click.Y}) right-click");
        }
        else if (click.Button == MouseButton.Middle)
        {
            Win32Api.PostMessage(hwnd, Win32Api.WM_MBUTTONDOWN, (IntPtr)Win32Api.MK_MBUTTON, lParam);
            await Task.Delay(50, token);
            Win32Api.PostMessage(hwnd, Win32Api.WM_MBUTTONUP, IntPtr.Zero, lParam);
            Log?.Invoke($"[Click/Stealth] ({click.X},{click.Y}) middle-click");
        }
        else
        {
            Win32Api.PostMessage(hwnd, Win32Api.WM_LBUTTONDOWN, (IntPtr)Win32Api.MK_LBUTTON, lParam);
            await Task.Delay(50, token);
            Win32Api.PostMessage(hwnd, Win32Api.WM_LBUTTONUP, IntPtr.Zero, lParam);
            Log?.Invoke($"[Click/Stealth] ({click.X},{click.Y})");
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
                        OnLog(LanguageManager.GetString("ui_Engine_OcrLowConfidence"));
                    if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        OnLog(string.Format(LanguageManager.GetString("ui_Engine_OcrMatchedAfter"), elapsed));
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
                        OnLog(string.Format(LanguageManager.GetString("ui_Engine_WaitImageFound"), elapsed));
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

        // FIX 5: HWND liveness check
        if (!IsTargetValid()) return;

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

        // FIX 5: HWND liveness check
        if (!IsTargetValid()) return;

        IntPtr target = Win32Api.FindInputChild(hwnd);
        bool isChild = target != hwnd;
        string suffix = isChild
            ? $" → child 0x{target:X} [{Win32Api.GetWindowClassName(target)}]"
            : "";

        string text = ExpandRuntime(type.Text);
        if (string.IsNullOrEmpty(text))
        {
            OnLog(LanguageManager.GetString("ui_Engine_TypeTextEmpty"));
            return;
        }

        bool stealthMode = _currentScript?.StealthMode ?? false;

        // ── HARDWARE MODE: SendInput Unicode per character ────────────────────────
        // User explicitly chose to take over mouse/keyboard for games or apps
        // that require hardware-level input.
        if (HardwareMode)
        {
            OnLog($"  TypeText/SendInput+Attach \"{Truncate(text, 40)}\"{suffix}");
            await TypeViaSendInputAsync(hwnd, text, type.KeyDelayMs, token);
            return;
        }

        // ── STEALTH MODE: PostMessage only (no SendInput) ──────────────────────
        // Target window is hidden/minimized; user can use their PC.
        // Electron apps in stealth mode: PostMessage WM_CHAR (best effort, may not work).
        if (stealthMode)
        {
            OnLog($"  TypeText/Stealth \"{Truncate(text, 40)}\"{suffix}");
            await TypeViaStealthTextAsync(target, text, type.KeyDelayMs, token);
            return;
        }

        // ── ELECTRON / CHROMIUM: UIAutomation type (stealth, no foreground steal) ─
        if (RequiresSendInput(hwnd))
        {
            await UiaTypeAsync(hwnd, text, token);
            return;
        }

        // ── NORMAL WIN32 APPS: PostMessage ─────────────────────────────────────
        // Target window is visible and foreground; PostMessage works perfectly.
        // User can still use their PC while macro runs (PostMessage doesn't block).
        if (type.InputMethod == TypeInputMethod.Clipboard || type.KeyDelayMs <= 0)
        {
            await TypeViaClipboardAsync(target, text, token);
        }
        else
        {
            await TypeViaWmCharAsync(target, text, type.KeyDelayMs, token);
        }

        OnLog($"  TypeText \"{Truncate(text, 40)}\"{suffix}");
    }

    /// <summary>
    /// Sends text via PostMessage in stealth mode (window hidden/minimized).
    /// Uses WM_CHAR for printable characters — best-effort for Electron apps
    /// since PostMessage may be ignored by the Chromium renderer.
    /// </summary>
    private async Task TypeViaStealthTextAsync(IntPtr target, string text, int delayMs, CancellationToken token)
    {
        foreach (char c in text)
        {
            token.ThrowIfCancellationRequested();
            Win32Api.PostMessage(target, Win32Api.WM_CHAR, (IntPtr)c, IntPtr.Zero);
            await Task.Delay(Math.Max(delayMs, 30), token);
        }
        OnLog(string.Format(LanguageManager.GetString("ui_Engine_WmCharLog"), text.Length, delayMs));
    }

    /// <summary>
    /// Electron-aware clipboard paste using SendInput Ctrl+V (NOT PostMessage).
    /// Discord ignores PostMessage but accepts Ctrl+V from SendInput.
    /// Sets foreground window first, then pastes text via clipboard.
    /// </summary>
    private async Task TypeViaClipboardAndPasteAsync(IntPtr hwnd, string text, CancellationToken token)
    {
        // FIX 1: Lock OS resources (Clipboard + AttachThreadInput) to prevent cross-window contamination
        if (!await _osResourceLock.WaitAsync(TimeSpan.FromSeconds(5), token))
        {
            OnLog("[WARN] OS resource lock timeout on TypeViaClipboardAndPasteAsync — skipping");
            return;
        }
        try
        {
            // Step 1: Set clipboard on UI thread
            await WpfApp.Current.Dispatcher.InvokeAsync(() =>
            {
                try { System.Windows.Clipboard.SetText(text); } catch { }
            });
            await Task.Delay(80, token);

            // Step 2: Attach to Discord's thread so PostMessage reaches its hidden window
            uint targetThread = GetWindowThreadProcessId(hwnd, out _);
            uint currentThread = GetCurrentThreadId();
            bool attached = false;
            if (targetThread != currentThread)
            {
                attached = AttachThreadInput(currentThread, targetThread, true);
                await Task.Delay(20, token);
            }

            try
            {
                // Step 3a: Send WM_PASTE to main window
                Win32Api.PostMessage(hwnd, WM_PASTE, IntPtr.Zero, IntPtr.Zero);
                await Task.Delay(100, token);

                // Step 3b: Also try child windows (Electron renderer)
                EnumChildWindowsWithLogging(hwnd);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(currentThread, targetThread, false);
            }

            OnLog(string.Format(LanguageManager.GetString("ui_Engine_ClipboardPasteElectron"), text.Length));
        }
        finally { _osResourceLock.Release(); }
    }

    /// <summary>
    /// Enumerates child windows of an Electron window and posts WM_PASTE to
    /// Chromium renderer children that accept it.
    /// </summary>
    private void EnumChildWindowsWithLogging(IntPtr parent)
    {
        var targetClass = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Chrome_RenderWidgetHostHWND",
            "Intermediate",
        };

        EnumChildWindows(parent, (child, _) =>
        {
            string cls = Win32Api.GetWindowClassName(child);
            if (cls.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("Intermediate", StringComparison.OrdinalIgnoreCase))
            {
                Win32Api.PostMessage(child, WM_PASTE, IntPtr.Zero, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
    }

    private async Task TypeViaClipboardAsync(IntPtr target, string text, CancellationToken token)
    {
        // FIX 1: Lock clipboard access to prevent cross-window contamination
        if (!await _osResourceLock.WaitAsync(TimeSpan.FromSeconds(5), token))
        {
            OnLog("[WARN] OS resource lock timeout on TypeViaClipboardAsync — skipping");
            return;
        }
        try
        {
            string? prev = null;

            await WpfApp.Current.Dispatcher.InvokeAsync(() =>
            {
                try { prev = System.Windows.Clipboard.GetText(); } catch { }
                try { System.Windows.Clipboard.SetText(text); } catch { }
            });

            await Task.Delay(100, token);

            PostMessage(target, WM_PASTE, IntPtr.Zero, IntPtr.Zero);

            await Task.Delay(150, token);

            await WpfApp.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (prev != null)
                        System.Windows.Clipboard.SetText(prev);
                    else
                        System.Windows.Clipboard.Clear();
                }
                catch { }
            });
        }
        finally { _osResourceLock.Release(); }

        OnLog(string.Format(LanguageManager.GetString("ui_Engine_ClipboardPaste"), text.Length));
    }

    private async Task TypeViaWmCharAsync(IntPtr target, string text, int delayMs, CancellationToken token)
    {
        foreach (char c in text)
        {
            token.ThrowIfCancellationRequested();
            Win32Api.PostMessage(target, Win32Api.WM_CHAR, (IntPtr)c, IntPtr.Zero);
            await Task.Delay(Math.Max(delayMs, 30), token);
        }
        OnLog(string.Format(LanguageManager.GetString("ui_Engine_WmCharLog2"), text.Length, delayMs));
    }

    /// <summary>
    /// Sends text via SendInput (Unicode mode) — works on Electron, Chromium, Java, Unity apps
    /// that block PostMessage. Uses AttachThreadInput so the window does NOT need to be
    /// foreground or visible — fully compatible with Stealth Mode.
    /// IMPORTANT: Detach happens IMMEDIATELY after SendInput, before any await.
    /// </summary>
    private async Task TypeViaSendInputAsync(IntPtr hwnd, string text, int delayMs, CancellationToken token)
    {
        // FIX 1: Lock SendInput usage to prevent cross-window contamination
        if (!await _osResourceLock.WaitAsync(TimeSpan.FromSeconds(5), token))
        {
            OnLog("[WARN] OS resource lock timeout on TypeViaSendInput — skipping");
            return;
        }
        try
        {
            uint targetThreadId = GetWindowThreadProcessId(hwnd, out _);
            uint ourThreadId = GetCurrentThreadId();

            foreach (char ch in text)
            {
                token.ThrowIfCancellationRequested();

                // Attach → send → detach atomically (no await while attached)
                if (AttachThreadInput(ourThreadId, targetThreadId, true))
                {
                    IntPtr focusedChild = GetFocusedChildWindow(hwnd);
                    SetFocus(focusedChild);

                    var down = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_SCANCODE, time = 0, dwExtraInfo = IntPtr.Zero } }
                    };
                    var up = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero } }
                    };

                    SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
                    ReleaseAllModifiers(); // safety: unstick any leaked modifiers
                    AttachThreadInput(ourThreadId, targetThreadId, false); // detach immediately
                }

                await Task.Delay(Math.Max(delayMs, 30), token);
            }
        }
        finally { _osResourceLock.Release(); }

        OnLog(string.Format(LanguageManager.GetString("ui_Engine_SendInputUnicode"), text.Length));
    }

    // ═══════════════════════════════════════════════
    //  SENDINPUT KEY PRESS (for Chrome, Electron, games)
    // ═══════════════════════════════════════════════

    /// <summary>
    /// Sends a key press via SendInput using VirtualKey + ScanCode.
    /// Detaches IMMEDIATELY after SendInput (before any await) to prevent modifier state leaks.
    /// </summary>
    private async Task ExecuteKeyPressSendInputAsync(KeyPressAction kpa, IntPtr hwnd, CancellationToken token)
    {
        // FIX 1: Lock SendInput usage to prevent cross-window contamination
        if (!await _osResourceLock.WaitAsync(TimeSpan.FromSeconds(5), token))
        {
            OnLog("[WARN] OS resource lock timeout on SendInput keypress — skipping");
            return;
        }
        try
        {
            uint targetThreadId = GetWindowThreadProcessId(hwnd, out _);
            uint ourThreadId = GetCurrentThreadId();

            // Find focused child once (stable for the duration of a single key press)
            IntPtr focusedChild;
            if (AttachThreadInput(ourThreadId, targetThreadId, true))
            {
                focusedChild = GetFocusedChildWindow(hwnd);
                SetFocus(focusedChild);

                var inputs = new List<INPUT>();

                void AddKey(ushort vk, bool keyUp)
                {
                    uint scanCode = MapVirtualKey(vk, 0);
                    inputs.Add(new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new INPUTUNION
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = vk,
                                wScan = (ushort)scanCode,
                                dwFlags = (keyUp ? KEYEVENTF_KEYUP : 0) | KEYEVENTF_SCANCODE,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    });
                }

                // Modifiers down
                if (kpa.Modifiers.Shift) AddKey(0x10, false);
                if (kpa.Modifiers.Ctrl) AddKey(0x11, false);
                if (kpa.Modifiers.Alt) AddKey(0x12, false);

                // Main key down + up
                AddKey((ushort)kpa.VirtualKeyCode, false);
                AddKey((ushort)kpa.VirtualKeyCode, true);

                // Modifiers up (reverse)
                if (kpa.Modifiers.Alt) AddKey(0x12, true);
                if (kpa.Modifiers.Ctrl) AddKey(0x11, true);
                if (kpa.Modifiers.Shift) AddKey(0x10, true);

                SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
                ReleaseAllModifiers(); // safety: unstick any leaked modifiers
                AttachThreadInput(ourThreadId, targetThreadId, false); // detach immediately
            }
            else
            {
                focusedChild = hwnd;
            }

            await Task.Delay(Math.Max(kpa.HoldDurationMs, 50), token);
            OnLog($"  KeyPress/SendInput+Attach {kpa.KeyName} → child: 0x{focusedChild:X}");
        }
        finally { _osResourceLock.Release(); }
    }

    /// <summary>
    /// Sends a key press via SendInput using pure ScanCode (no VirtualKey).
    /// For DirectX games and Anti-Cheat systems. Detaches immediately after SendInput.
    /// </summary>
    private async Task ExecuteKeyPressRawInputAsync(KeyPressAction kpa, IntPtr hwnd, CancellationToken token)
    {
        // FIX 1: Lock SendInput usage to prevent cross-window contamination
        if (!await _osResourceLock.WaitAsync(TimeSpan.FromSeconds(5), token))
        {
            OnLog("[WARN] OS resource lock timeout on RawInput keypress — skipping");
            return;
        }
        try
        {
            uint targetThreadId = GetWindowThreadProcessId(hwnd, out _);
            uint ourThreadId = GetCurrentThreadId();

            IntPtr focusedChild;
            if (AttachThreadInput(ourThreadId, targetThreadId, true))
            {
                focusedChild = GetFocusedChildWindow(hwnd);
                SetFocus(focusedChild);

                var inputs = new List<INPUT>();

                void AddScanCode(int vk, bool keyUp)
                {
                    uint sc = MapVirtualKey((uint)vk, 0);
                    bool isExtended = vk is 0x21 or 0x22 or 0x23 or 0x24 or
                                          0x25 or 0x26 or 0x27 or 0x28 or
                                          0x2D or 0x2E or 0xA1 or 0xA3 or 0xA5;
                    uint flags = KEYEVENTF_SCANCODE;
                    if (keyUp) flags |= KEYEVENTF_KEYUP;
                    if (isExtended) flags |= 0x0001;
                    inputs.Add(new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new INPUTUNION
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = 0,
                                wScan = (ushort)sc,
                                dwFlags = flags,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    });
                }

                if (kpa.Modifiers.Shift) AddScanCode(0x10, false);
                if (kpa.Modifiers.Ctrl) AddScanCode(0x11, false);
                if (kpa.Modifiers.Alt) AddScanCode(0x12, false);

                AddScanCode(kpa.VirtualKeyCode, false);
                AddScanCode(kpa.VirtualKeyCode, true);

                if (kpa.Modifiers.Alt) AddScanCode(0x12, true);
                if (kpa.Modifiers.Ctrl) AddScanCode(0x11, true);
                if (kpa.Modifiers.Shift) AddScanCode(0x10, true);

                SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
                ReleaseAllModifiers();
                AttachThreadInput(ourThreadId, targetThreadId, false);
            }
            else
            {
                focusedChild = hwnd;
            }

            await Task.Delay(Math.Max(kpa.HoldDurationMs, 50), token);
            OnLog($"[KeyPress/RawInput+Attach] {kpa.KeyName} SC=0x{MapVirtualKey((uint)kpa.VirtualKeyCode, 0):X2} → child: 0x{focusedChild:X}");
        }
        finally { _osResourceLock.Release(); }
    }

    /// <summary>
    /// Sends a key press via Interception driver — kernel-level HID emulation.
    /// Bypasses anti-cheat (HackShield, NGS, EAC). Falls back to RawInput if driver not installed.
    /// </summary>
    private async Task ExecuteKeyPressDriverLevelAsync(KeyPressAction kpa, IntPtr hwnd, CancellationToken token)
    {
        if (!await _osResourceLock.WaitAsync(TimeSpan.FromSeconds(5), token))
        {
            OnLog("[WARN] OS resource lock timeout on keypress/Driver — skipping");
            return;
        }
        try
        {
            if (!InterceptionService.Instance.IsInitialized)
            {
                OnLog(string.Format(LanguageManager.GetString("ui_Engine_InterceptionNotInstalled"), "RawInput"));
                // Reuse existing RawInput logic inline (without nested lock — already holding it)
                uint targetThreadId = GetWindowThreadProcessId(hwnd, out _);
                uint ourThreadId = GetCurrentThreadId();

                IntPtr focusedChild;
                if (AttachThreadInput(ourThreadId, targetThreadId, true))
                {
                    focusedChild = GetFocusedChildWindow(hwnd);
                    SetFocus(focusedChild);

                    var inputs = new List<INPUT>();
                    void AddKey(int v, bool keyUp)
                    {
                        uint sc = MapVirtualKey((uint)v, 0);
                        bool isExt = v is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0xA1 or 0xA3 or 0xA5;
                        uint flags = KEYEVENTF_SCANCODE;
                        if (keyUp) flags |= KEYEVENTF_KEYUP;
                        if (isExt) flags |= 0x0001;
                        inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0, wScan = (ushort)sc, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero } } });
                    }

                    if (kpa.Modifiers.Shift) AddKey(0x10, false);
                    if (kpa.Modifiers.Ctrl) AddKey(0x11, false);
                    if (kpa.Modifiers.Alt) AddKey(0x12, false);
                    AddKey(kpa.VirtualKeyCode, false);
                    AddKey(kpa.VirtualKeyCode, true);
                    if (kpa.Modifiers.Alt) AddKey(0x12, true);
                    if (kpa.Modifiers.Ctrl) AddKey(0x11, true);
                    if (kpa.Modifiers.Shift) AddKey(0x10, true);

                    SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
                    ReleaseAllModifiers();
                    AttachThreadInput(ourThreadId, targetThreadId, false);
                }
                await Task.Delay(Math.Max(kpa.HoldDurationMs, 50), token);
                OnLog($"[KeyPress/Driver→RawInput fallback] {kpa.KeyName}");
                return;
            }

            // ── Driver level path ──
            uint scanCode = MapVirtualKey((uint)kpa.VirtualKeyCode, 0);
            bool isExtended = kpa.VirtualKeyCode is 0x21 or 0x22 or 0x23 or 0x24 or
                                          0x25 or 0x26 or 0x27 or 0x28 or
                                          0x2D or 0x2E or 0xA1 or 0xA3 or 0xA5;

            if (kpa.Modifiers.Shift) InterceptionService.Instance.SendKey(0x2A, true);  // LShift scan
            if (kpa.Modifiers.Ctrl) InterceptionService.Instance.SendKey(0x1D, true, extended: true);
            if (kpa.Modifiers.Alt) InterceptionService.Instance.SendKey(0x38, true, extended: true);

            InterceptionService.Instance.TapKey((ushort)scanCode, Math.Max(kpa.HoldDurationMs, 50), isExtended);

            if (kpa.Modifiers.Alt) InterceptionService.Instance.SendKey(0x38, false, extended: true);
            if (kpa.Modifiers.Ctrl) InterceptionService.Instance.SendKey(0x1D, false, extended: true);
            if (kpa.Modifiers.Shift) InterceptionService.Instance.SendKey(0x2A, false);

            OnLog($"[KeyPress/Driver] {kpa.KeyName} SC=0x{scanCode:X2}");
        }
        finally { _osResourceLock.Release(); }
    }

    /// <summary>
    /// Safety: releases all modifier keys (Shift, Ctrl, Alt) after SendInput.
    /// Sends KEYUP for all modifiers regardless of whether they were pressed,
    /// to unstick any leaked modifier state from previous actions.
    /// Called after EVERY SendInput block while still attached.
    /// </summary>
    private void ReleaseAllModifiers()
    {
        var safetyInputs = new[]
        {
            new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT { wVk = 0x10, wScan = 0, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            },
            new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT { wVk = 0x11, wScan = 0, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            },
            new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT { wVk = 0x12, wScan = 0, dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            },
        };
        SendInput(3, safetyInputs, Marshal.SizeOf<INPUT>());
    }

    private async Task ExecuteKeyPressAsync(KeyPressAction kpa, CancellationToken token)
    {
        if (kpa.VirtualKeyCode <= 0)
        {
            OnLog("  KeyPress — SKIPPED (no key set)");
            return;
        }

        IntPtr hwnd = _runtimeTargetHwnd;

        // FIX 5: HWND liveness check
        if (!IsTargetValid()) return;

        int vk = kpa.VirtualKeyCode;
        int hold = Math.Max(0, kpa.HoldDurationMs);

        // ── HARDWARE MODE: SendInput or RawInput ─────────────────────────────────
        switch (kpa.InputMode)
        {
            case KeyInputMode.RawInput:
                await ExecuteKeyPressRawInputAsync(kpa, hwnd, token);
                return;
            case KeyInputMode.SendInput:
                await ExecuteKeyPressSendInputAsync(kpa, hwnd, token);
                return;
            case KeyInputMode.DriverLevel:
                await ExecuteKeyPressDriverLevelAsync(kpa, hwnd, token);
                return;
        }

        // ── POSTMESSAGE PATH (FIX 4) ─────────────────────────────────────────────
        // Route based on key complexity to minimize cross-window contamination risk.

        // PATH A: Simple printable char (no Ctrl/Alt) → PostMessage WM_CHAR directly.
        // Zero cross-contamination risk — targets specific HWND, no focus steal needed.
        // Uses GetFocusedChildForTarget which validates PID + IsChild to avoid wrong Discord window.
        if (!kpa.Modifiers.Ctrl && !kpa.Modifiers.Alt)
        {
            char? printable = TryGetPrintableChar(vk, kpa.Modifiers.Shift, kpa.Modifiers.Ctrl, kpa.Modifiers.Alt);
            if (printable.HasValue)
            {
                IntPtr child = GetFocusedChildForTarget(hwnd);
                PostMessage(child, WM_CHAR, (IntPtr)printable.Value, IntPtr.Zero);
                await Task.Delay(hold, token).ConfigureAwait(false);
                OnLog($"[KeyPress/PostChar] '{printable.Value}' → 0x{child:X}");
                return;
            }
        }

        // PATH B: Control keys without Shift/Alt → PostMessage WM_KEYDOWN directly.
        // No cross-contamination risk — targets specific HWND via GetFocusedChildForTarget.
        if (!kpa.Modifiers.Shift && !kpa.Modifiers.Alt)
        {
            IntPtr child = GetFocusedChildForTarget(hwnd);
            uint scan = MapVirtualKey((uint)vk, 0);
            IntPtr lpDn = (IntPtr)((scan << 16) | 1);
            IntPtr lpUp = (IntPtr)((scan << 16) | 0xC0000001);

            if (kpa.Modifiers.Ctrl)
            {
                PostMessage(child, WM_KEYDOWN, (IntPtr)0x11, MakeLParam(0x1D, 0));
                await Task.Delay(15, token);
            }
            PostMessage(child, WM_KEYDOWN, (IntPtr)vk, lpDn);
            await Task.Delay(Math.Max(hold, 20), token);
            PostMessage(child, WM_KEYUP, (IntPtr)vk, lpUp);
            if (kpa.Modifiers.Ctrl)
            {
                await Task.Delay(15, token);
                PostMessage(child, WM_KEYUP, (IntPtr)0x11, MakeLParam(0x1D, 0xC000));
            }
            OnLog($"[KeyPress/PostMsg] {kpa.KeyName} → 0x{child:X}");
            return;
        }

        // PATH C: Complex combos (Shift+Alt etc.) → AttachThreadInput fallback.
        // Note: If Ctrl+V still misfires here, Chromium calls GetKeyState() physically
        // → route those through TypeViaFlashForegroundAsync (which has _osResourceLock).
        await ExecuteKeyPressAttachAsync(hwnd, kpa, token);
    }

    /// <summary>
    /// PATH C fallback: AttachThreadInput for complex modifier combos (Shift+Alt, etc.)
    /// that can't be safely routed via PostMessage alone.
    /// </summary>
    private async Task ExecuteKeyPressAttachAsync(IntPtr hwnd, KeyPressAction kpa, CancellationToken token)
    {
        uint ourThread = GetCurrentThreadId();
        IntPtr focused = hwnd;

        bool attached = AttachThreadInput(ourThread, _targetThread, true);
        try
        {
            IntPtr raw = GetFocus();
            if (raw != IntPtr.Zero)
            {
                GetWindowThreadProcessId(raw, out uint focusedPid);
                // Validate — ONLY proceed if focused window belongs to OUR target
                bool valid = focusedPid == _targetPid &&
                             (raw == hwnd || IsChild(hwnd, raw));
                focused = valid ? raw : hwnd;
            }
        }
        finally
        {
            if (attached) AttachThreadInput(ourThread, _targetThread, false);
        }

        // For Electron/Chromium: use ElectronKeyPressAsync (which holds _osResourceLock)
        if (RequiresSendInput(hwnd))
        {
            await ElectronKeyPressAsync(focused, kpa, token);
            return;
        }

        // Normal Win32 apps: PostMessage works.
        IntPtr downParam = BuildKeyLParam(kpa.VirtualKeyCode, isKeyUp: false);
        IntPtr upParam   = BuildKeyLParam(kpa.VirtualKeyCode, isKeyUp: true);
        PostMessage(focused, WM_KEYDOWN, (IntPtr)kpa.VirtualKeyCode, downParam);
        await Task.Delay(Math.Max(kpa.HoldDurationMs, 50), token).ConfigureAwait(false);
        PostMessage(focused, WM_KEYUP, (IntPtr)kpa.VirtualKeyCode, upParam);
        OnLog($"[KeyPress/Attach] {kpa.KeyName} → 0x{focused:X}");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private static IntPtr MakeLParam(int lo, int hi) =>
        (IntPtr)((hi << 16) | (lo & 0xFFFF));

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

        // FIX 5: HWND liveness check
        if (!IsTargetValid()) return;

        string imagePath = ExpandRuntime(ifImage.ImagePath);
        int maxWait = Math.Max(0, ifImage.TimeoutMs);
        int elapsed = 0;
        int retryCount = 0;
        Point? match = null;

        bool retryUntilFound = ifImage.RetryUntilFound;
        int retryInterval = Math.Max(50, ifImage.RetryIntervalMs);
        int maxRetries = ifImage.MaxRetryCount; // 0 = unlimited

        OnLog($"  IfImageFound \"{Path.GetFileName(imagePath)}\" " +
              $"(threshold={ifImage.Threshold:P0}, timeout={maxWait}ms, retryUntilFound={retryUntilFound})");

        // ── RetryUntilFound mode: loop indefinitely until found or MaxRetries hit ────────
        if (retryUntilFound)
        {
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
                {
                    OnLog($"    → FOUND at ({match.Value.X}, {match.Value.Y}) after {retryCount} retry(ies)");
                    break;
                }

                // Check retry limit before waiting
                if (maxRetries > 0 && retryCount >= maxRetries)
                {
                    OnLog($"    → Max retry count ({maxRetries}) reached → running ElseActions");
                    match = null;
                    break;
                }

                retryCount++;
                OnLog($"    [Retry {retryCount}] Not found — waiting {retryInterval}ms...");
                await _macroRunner.Timing.WaitAsync(retryInterval, Math.Max(10, retryInterval / 4), token).ConfigureAwait(false);
            }
            while (true);
        }
        // ── Standard timeout-based mode ──────────────────────────────────────────────────
        else
        {
            const int PollMs = 500;

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
                OnLog($"    → FOUND at ({match.Value.X}, {match.Value.Y}) after {elapsed}ms");
        }

        // ── Image found → click (if enabled) + ThenActions ───────────────────────────────
        if (match.HasValue)
        {
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
                int ox = Random.Shared.Next(-off, off + 1);
                int oy = Random.Shared.Next(-off, off + 1);
                int cx = match.Value.X + ox;
                int cy = match.Value.Y + oy;

                // Route by per-action ClickMode, not global HardwareMode
                switch (ifImage.ClickMode)
                {
                    case ClickMode.Hardware:
                        {
                            Point screen = Win32Api.ClientPointToScreen(hwnd, cx, cy);
                            MouseProfile profile = BezierMouseMover.ParseProfile(AppSettings.Load().MouseProfileName);
                            OnLog($"    → HW click at ({cx},{cy}) screen ({screen.X},{screen.Y})");
                            await _mouseMover.MoveAndClickAsync(screen, MouseButton.Left, profile, token).ConfigureAwait(false);
                            break;
                        }
                    case ClickMode.Raw:
                        {
                            // SendInput — hijacks physical cursor
                            if (!await _osResourceLock.WaitAsync(TimeSpan.FromSeconds(5), token)) return;
                            try
                            {
                                var pt = new POINT { X = cx, Y = cy };
                                ClientToScreen(hwnd, ref pt);
                                int screenW = GetSystemMetrics(0);
                                int screenH = GetSystemMetrics(1);
                                int absX = (int)((pt.X + 0.5) * 65536.0 / screenW);
                                int absY = (int)((pt.Y + 0.5) * 65536.0 / screenH);
                                var inputs = new[]
                                {
                                    new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dx = absX, dy = absY, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESKTOP } } },
                                    new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } },
                                    new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } }
                                };
                                SendInput(3, inputs, Marshal.SizeOf<INPUT>());
                                OnLog($"    → Raw click at ({cx},{cy})");
                            }
                            finally { _osResourceLock.Release(); }
                            break;
                        }
                    default: // ClickMode.Stealth
                        {
                            await Win32Api.StealthClickOnFoundImage(hwnd, new Point(cx, cy), randomOffsetRange: 0, token);
                            OnLog($"    → Stealth click at ({cx},{cy}) [offset ±{off}px]");
                            break;
                        }
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
            OnLog($"    → NOT FOUND → running ElseActions");

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

        // FIX 5: HWND liveness check
        if (!IsTargetValid()) return;

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
        InitializeTargetHwnd(found); // FIX 2: pin new HWND after launch & bind
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
            OnLog(LanguageManager.GetString("ui_Engine_TelegramSkippedEmpty"));
            return Task.CompletedTask;
        }

        string rawMessage = tg.Message ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            OnLog(LanguageManager.GetString("ui_Engine_TelegramSkippedEmptyMsg"));
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
                Log?.Invoke(string.Format(LanguageManager.GetString("ui_Engine_TelegramFail"), ex.Message));
            }
        });
    }

    // ═══════════════════════════════════════════════
    //  SENDINPUT (for Chrome, Electron, DirectX games)
    // ═══════════════════════════════════════════════

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    private const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, Win32Api.EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// Sends a PostMessage click to Electron/Chromium windows instead of using real mouse.
    /// Works on hidden windows via AttachThreadInput. Falls back to child windows.
    /// </summary>
    private async Task ElectronClickAsync(IntPtr hwnd, int x, int y, bool rightClick, CancellationToken token)
    {
        IntPtr lParam = Win32Api.MakeLParam(x, y);

        // Attach thread to target so PostMessage reaches hidden window
        uint targetThread = GetWindowThreadProcessId(hwnd, out _);
        uint currentThread = GetCurrentThreadId();
        bool attached = targetThread != currentThread && AttachThreadInput(currentThread, targetThread, true);
        await Task.Delay(20, token);

        try
        {
            // Try main window first
            if (rightClick)
            {
                Win32Api.PostMessage(hwnd, Win32Api.WM_RBUTTONDOWN, (IntPtr)Win32Api.MK_RBUTTON, lParam);
                await Task.Delay(30, token);
                Win32Api.PostMessage(hwnd, Win32Api.WM_RBUTTONUP, IntPtr.Zero, lParam);
            }
            else
            {
                Win32Api.PostMessage(hwnd, Win32Api.WM_LBUTTONDOWN, (IntPtr)1, lParam);
                await Task.Delay(30, token);
                Win32Api.PostMessage(hwnd, Win32Api.WM_LBUTTONUP, IntPtr.Zero, lParam);
            }
            await Task.Delay(50, token);

            // Also try Electron renderer child windows
            EnumChildWindowsForClick(hwnd, x, y, rightClick, token);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, targetThread, false);
        }
    }

    /// <summary>
    /// Enumerates Electron renderer child windows and sends PostMessage click to them.
    /// </summary>
    private void EnumChildWindowsForClick(IntPtr parent, int x, int y, bool rightClick, CancellationToken token)
    {
        IntPtr lParam = Win32Api.MakeLParam(x, y);
        IntPtr down = rightClick ? (IntPtr)Win32Api.MK_RBUTTON : (IntPtr)Win32Api.MK_LBUTTON;
        uint downMsg = rightClick ? Win32Api.WM_RBUTTONDOWN : Win32Api.WM_LBUTTONDOWN;
        uint upMsg = rightClick ? Win32Api.WM_RBUTTONUP : Win32Api.WM_LBUTTONUP;

        EnumChildWindows(parent, (child, _) =>
        {
            string cls = Win32Api.GetWindowClassName(child);
            if (cls.Contains("Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("Intermediate", StringComparison.OrdinalIgnoreCase))
            {
                Win32Api.PostMessage(child, downMsg, down, lParam);
                Thread.Sleep(20);
                Win32Api.PostMessage(child, upMsg, IntPtr.Zero, lParam);
            }
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>
    /// Finds the actual focused child window within the target process using AttachThreadInput.
    /// For Electron apps (Discord, etc.), the top-level window is NOT the actual text input —
    /// the focused window is deep in the Chromium render tree.
    /// Returns <paramref name="topLevelHwnd"/> if no valid child is found.
    /// FIX 3: Uses native IsChild() instead of GetAncestor() walk for speed and reliability.
    /// </summary>
    private IntPtr GetFocusedChildForTarget(IntPtr topLevelHwnd)
    {
        uint ourThread = GetCurrentThreadId();
        IntPtr result = topLevelHwnd;

        bool attached = AttachThreadInput(ourThread, _targetThread, true);
        try
        {
            IntPtr focused = GetFocus();
            if (focused != IntPtr.Zero)
            {
                GetWindowThreadProcessId(focused, out uint focusedPid);
                // Must belong to same process AND be child of our specific top-level window
                if (focusedPid == _targetPid &&
                    (focused == topLevelHwnd || IsChild(topLevelHwnd, focused)))
                {
                    result = focused;
                }
                // else: belongs to another Discord window → stay with topLevelHwnd
            }
        }
        finally
        {
            if (attached) AttachThreadInput(ourThread, _targetThread, false);
        }
        return result;
    }

    // Legacy alias kept for backward compatibility with existing callers
    private IntPtr GetFocusedChildWindow(IntPtr topLevelHwnd) => GetFocusedChildForTarget(topLevelHwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    // Virtual key codes used by keybd_event in TypeViaFlashForegroundAsync
    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

    // Created by Phạm Duy – Giải pháp tự động hóa thông minh.
    private const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP     = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
    private const uint MOUSEEVENTF_MOVE       = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE    = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESKTOP = 0x4000;
    private const uint INPUT_MOUSE = 0;

    private static IntPtr MAKELPARAM(int x, int y)
        => (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));

    /// <summary>
    /// Detects non-Win32 apps that ignore PostMessage input.
    /// Discord (Electron/Chromium), Java Swing, and Unity all fall in this category.
    /// </summary>
    private static bool RequiresSendInput(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var sb = new System.Text.StringBuilder(256);
        int len = GetClassName(hwnd, sb, 256);
        if (len == 0) return false;
        string className = sb.ToString().ToLowerInvariant();
        return className.Contains("chrome_") ||
               className.Contains("chromewidget") ||
               className.Contains("chrome_widgetwin") ||  // Discord / Electron top-level windows
               className.Contains("cef") ||
               className.Contains("sunawt") ||
               className.Contains("unitywnd") ||
               className.Contains("afx:") ||
               className.Contains("rwidget");
    }

    // ═══════════════════════════════════════════════
    //  SUB-MACRO (CallMacroAction)
    // ═══════════════════════════════════════════════

    private async Task ExecuteCallMacroAsync(CallMacroAction cma, CancellationToken token)
    {
        // Guard: prevent infinite recursion from deep nesting
        if (_subMacroDepth >= MaxSubMacroDepth)
        {
            OnLog(string.Format(LanguageManager.GetString("ui_Engine_SubMacroTooDeep"), _subMacroDepth));
            return;
        }

        if (string.IsNullOrWhiteSpace(cma.MacroFilePath))
        {
            OnLog(LanguageManager.GetString("ui_Engine_SubMacroNoScript"));
            return;
        }

        string resolvedPath = VariableResolver.Resolve(cma.MacroFilePath, _runtimeVariables);
        if (!File.Exists(resolvedPath))
        {
            OnLog(string.Format(LanguageManager.GetString("ui_Engine_SubMacroFileNotFound"), resolvedPath));
            return;
        }

        // Guard: prevent calling itself
        if (_currentScript?.FilePath != null &&
            Path.GetFullPath(resolvedPath) == Path.GetFullPath(_currentScript.FilePath))
        {
            OnLog(LanguageManager.GetString("ui_Engine_SubMacroSelfCall"));
            return;
        }

        var subScript = ScriptManager.Load(resolvedPath);
        if (subScript == null)
        {
            OnLog(string.Format(LanguageManager.GetString("ui_Engine_SubMacroReadFailed"), resolvedPath));
            return;
        }

        OnLog(string.Format(LanguageManager.GetString("ui_Engine_SubMacroStarted"), _subMacroDepth + 1, MaxSubMacroDepth, cma.MacroName ?? subScript.Name));

        var subEngine = new MacroEngine(this, subScript, _runtimeTargetHwnd, Log);

        if (cma.PassVariables)
        {
            foreach (var kv in _runtimeVariables)
            {
                subEngine.Variables.Set(kv.Key, kv.Value, "Parent");
            }
        }

        if (cma.WaitForFinish)
        {
            await subEngine.ExecuteScriptAsync(subScript, _runtimeTargetHwnd, token).ConfigureAwait(false);
            OnLog(string.Format(LanguageManager.GetString("ui_Engine_SubMacroComplete"), _subMacroDepth + 1, cma.MacroName ?? subScript.Name));
        }
        else
        {
            _ = subEngine.ExecuteScriptAsync(subScript, _runtimeTargetHwnd, token);
            OnLog(string.Format(LanguageManager.GetString("ui_Engine_SubMacroLaunched"), cma.MacroName ?? subScript.Name));
        }
    }

    // ═══════════════════════════════════════════════
    //  UTIL
    // ═══════════════════════════════════════════════

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "…");

    // ═══════════════════════════════════════════════
    //  UIAUTOMATION — Electron / Chromium stealth input
    // ═══════════════════════════════════════════════

    /// <summary>
    /// UIAutomation-based click for Electron/Chromium apps (Discord, Chrome, VS Code).
    /// Uses AutomationElement.FromPoint to find exact element at coordinates,
    /// tries InvokePattern for buttons, then falls back to PostMessage.
    /// </summary>
    private async Task UiaClickAsync(IntPtr hwnd, int x, int y, CancellationToken token)
    {
        try
        {
            // Use ClientToScreen to convert client coords to screen coords (ignores DWM shadows)
            var pt = new Win32Api.POINT { X = x, Y = y };
            Win32Api.ClientToScreen(hwnd, ref pt);
            var screenPoint = new System.Windows.Point(pt.X, pt.Y);

            // Find exact element at coordinates
            AutomationElement? target = null;
            try { target = AutomationElement.FromPoint(screenPoint); } catch { }

            if (target != null)
            {
                // Try InvokePattern (button click)
                if (target.TryGetCurrentPattern(InvokePattern.Pattern, out var inv))
                {
                    ((InvokePattern)inv).Invoke();
                    Log?.Invoke($"[Click/UIA] ({x},{y}) → {target.Current.Name}");
                    return;
                }
            }
        }
        catch { }

        // Fallback: PostMessage after UIA SetFocus
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            element?.SetFocus();
            await Task.Delay(50, token);
        }
        catch { }

        IntPtr lParam = MAKELPARAM(x, y);
        Win32Api.PostMessage(hwnd, Win32Api.WM_LBUTTONDOWN, (IntPtr)1, lParam);
        await Task.Delay(30, token);
        Win32Api.PostMessage(hwnd, Win32Api.WM_LBUTTONUP, IntPtr.Zero, lParam);
        Log?.Invoke($"[Click/UIA+Post] ({x},{y})");
    }

    /// <summary>
    /// UIAutomation-based type for Electron/Chromium apps (Discord, Chrome, VS Code).
    /// Tries ValuePattern first (works for simple Win32 inputs, NOT Discord/React).
    /// Falls back to flash foreground (safe for Electron/Discord/React).
    /// </summary>
    private async Task UiaTypeAsync(IntPtr hwnd, string text, CancellationToken token)
    {
        // Tier 1: ValuePattern — works for simple Win32 inputs, NOT Discord/React
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.IsEnabledProperty, true),
                new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)
                )
            );
            var textBox = root?.FindFirst(TreeScope.Descendants, condition);
            if (textBox != null &&
                textBox.TryGetCurrentPattern(ValuePattern.Pattern, out var vp) &&
                !((ValuePattern)vp).Current.IsReadOnly)
            {
                ((ValuePattern)vp).SetValue(text);
                Log?.Invoke($"[TypeText/UIA-Value] \"{Truncate(text, 40)}\"");
                return;
            }
        }
        catch { }

        // Tier 2: Flash foreground (only safe method for Electron/Discord/React)
        await TypeViaFlashForegroundAsync(hwnd, text, token);
    }
    private async Task ElectronKeyPressAsync(IntPtr hwnd, KeyPressAction kpa, CancellationToken token)
    {
        // FIX 1: Wrap OS resource usage (SetForegroundWindow + SendInput)
        if (!await _osResourceLock.WaitAsync(TimeSpan.FromSeconds(5), token))
        {
            OnLog("[WARN] OS resource lock timeout on keypress — skipping");
            return;
        }
        try
        {
            IntPtr prevFg = GetForegroundWindow();
            bool wasHidden = !Win32Api.IsWindowVisible(hwnd);

            if (wasHidden) { ShowWindow(hwnd, SW_SHOW); await Task.Delay(80, token); }
            SetForegroundWindow(hwnd);
            await Task.Delay(120, token);

            var inputs = new List<INPUT>();

            if (kpa.Modifiers?.Shift == true)
                inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0x10 } } });
            if (kpa.Modifiers?.Ctrl == true)
                inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0x11 } } });
            if (kpa.Modifiers?.Alt == true)
                inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0x12 } } });

            inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = (ushort)kpa.VirtualKeyCode } } });
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = (ushort)kpa.VirtualKeyCode, dwFlags = KEYEVENTF_KEYUP } } });

            if (kpa.Modifiers?.Alt == true)
                inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0x12, dwFlags = KEYEVENTF_KEYUP } } });
            if (kpa.Modifiers?.Ctrl == true)
                inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0x11, dwFlags = KEYEVENTF_KEYUP } } });
            if (kpa.Modifiers?.Shift == true)
                inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = 0x10, dwFlags = KEYEVENTF_KEYUP } } });

            var arr = inputs.ToArray();
            SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
            await Task.Delay(80, token);

            if (wasHidden) { ShowWindow(hwnd, SW_HIDE); await Task.Delay(30, token); }
            if (prevFg != IntPtr.Zero && prevFg != hwnd) SetForegroundWindow(prevFg);
        }
        finally { _osResourceLock.Release(); }

        Log?.Invoke($"[KeyPress/Flash] {kpa.KeyName}");
    }

    private async Task TypeViaFlashForegroundAsync(IntPtr hwnd, string text, CancellationToken token)
    {
        // FIX 1: Wrap OS resource usage (Clipboard + SetForegroundWindow + SendInput Ctrl+V)
        if (!await _osResourceLock.WaitAsync(TimeSpan.FromSeconds(5), token))
        {
            OnLog("[WARN] OS resource lock timeout — skipping to prevent deadlock");
            return;
        }
        try
        {
            string? prev = null;
            await WpfApp.Current.Dispatcher.InvokeAsync(() => {
                try { prev = System.Windows.Clipboard.GetText(); } catch {}
                try { System.Windows.Clipboard.SetText(text); } catch {}
            });
            await Task.Delay(20, token);

            IntPtr prevFg = GetForegroundWindow();
            if (hwnd != prevFg) { SetForegroundWindow(hwnd); await Task.Delay(50, token); }

            // SendInput Ctrl+V
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            await Task.Delay(30, token);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            if (prevFg != IntPtr.Zero && prevFg != hwnd) SetForegroundWindow(prevFg);

            await WpfApp.Current.Dispatcher.InvokeAsync(() => {
                try { if (prev != null) System.Windows.Clipboard.SetText(prev); else System.Windows.Clipboard.Clear(); } catch {}
            });
        }
        finally { _osResourceLock.Release(); }

        OnLog(string.Format(LanguageManager.GetString("ui_Engine_ClipboardPasteFlash"), text.Length));
    }
}
