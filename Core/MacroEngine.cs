using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
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
    private PlaywrightEngine? _playwrightEngine;

    private readonly BezierMouseMover _mouseMover = new();

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
        _runReport.Clear();

        _mouseMover.ReloadFromAppSettings();
        _mouseMover.SetRawInputNotifyWindow(_runtimeTargetHwnd);

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
        return _vars.Resolve(t);
    }

    // ═══════════════════════════════════════════════
    //  REPEAT LOOP
    // ═══════════════════════════════════════════════

    private async Task RunLoopAsync(MacroScript script, CancellationToken token)
    {
        bool infinite = script.RepeatCount <= 0;
        int totalIterations = infinite ? int.MaxValue : script.RepeatCount;

        MacroScriptValidation.ValidateRepeatAndLoopCsv(script);

        for (int i = 1; i <= totalIterations; i++)
        {
            token.ThrowIfCancellationRequested();

            if (_runtimeTargetHwnd != IntPtr.Zero && !Win32Api.IsWindow(_runtimeTargetHwnd))
                throw new InvalidOperationException("Target window was closed during execution.");

            ApplyIterationVariables(script, i);

            string label = infinite ? $"#{i} (infinite)" : $"#{i}/{script.RepeatCount}";
            OnLog($"── Iteration {label} ──");
            IterationStarted?.Invoke(i, script.RepeatCount);

            await ExecuteActionsAsync(script.Actions, token).ConfigureAwait(false);

            bool hasMore = i < totalIterations;
            if (hasMore && script.IntervalMinutes > 0)
            {
                OnLog($"── Waiting {script.IntervalMinutes} min before next iteration ──");
                await Task.Delay(TimeSpan.FromMinutes(script.IntervalMinutes), token);
            }
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
                    string resolved = ExpandRuntime(setVar.Value);
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

                    OnLog($"    → VAR {name} = {_vars.Get(name)} [{op}]");
                    break;
                }

                case IfVariableAction ifVar:
                {
                    string left = _vars.GetString(ifVar.VarName.Trim(), "");
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

                default:
                    OnLog($"  [{idx}] Unknown action type: {action.GetType().Name}");
                    break;
            }

            ActionCompleted?.Invoke(action, idx);
        }
    }

    // ═══════════════════════════════════════════════
    //  ACTION HANDLERS
    // ═══════════════════════════════════════════════

    private async Task ExecuteClickAsync(ClickAction click, CancellationToken token)
    {
        IntPtr hwnd = _runtimeTargetHwnd;

        if (!Win32Api.IsInsideClientArea(hwnd, click.X, click.Y))
            OnLog($"  ⚠ ({click.X},{click.Y}) is outside client rect");

        if (HardwareMode)
        {
            Point screen = Win32Api.ClientPointToScreen(hwnd, click.X, click.Y);
            MouseProfile profile = BezierMouseMover.ParseProfile(AppSettings.Load().MouseProfileName);
            var btn = click.IsRightClick ? MouseButton.Right : MouseButton.Left;
            OnLog($"  HardwareMove+Click {btn} → screen ({screen.X},{screen.Y}) profile={profile}");
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

                await Task.Delay(step, token).ConfigureAwait(false);
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

        await Task.Delay(ms, token).ConfigureAwait(false);
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
                await Task.Delay(repeat.IntervalMs, token).ConfigureAwait(false);
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
                await Task.Delay(type.KeyDelayMs, token);
            }
        }
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

            await Task.Delay(wait, token);
            elapsed += wait;
        } while (elapsed < maxWait);

        if (match.HasValue)
        {
            OnLog($"    → FOUND at ({match.Value.X}, {match.Value.Y}) after {elapsed}ms");

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

        _playwrightEngine ??= new PlaywrightEngine();
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

        _playwrightEngine ??= new PlaywrightEngine();
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
        _playwrightEngine ??= new PlaywrightEngine();
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
        _playwrightEngine ??= new PlaywrightEngine();
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
            await Task.Delay(pollMs, token).ConfigureAwait(false);

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
    //  UTIL
    // ═══════════════════════════════════════════════

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "…");
}
