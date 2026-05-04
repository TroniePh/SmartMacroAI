// Created by Phạm Duy – Giải pháp tự động hóa thông minh.

using System.Drawing;
using SmartMacroAI.Localization;

namespace SmartMacroAI.Core;

/// <summary>Optional micro-pauses, scroll nudge, session breaks — fail-safe (swallows errors).</summary>
public static class BehaviorRandomizer
{
    /// <summary>Probability [0..1] used by tests; production uses settings slider.</summary>
    public static bool RollMisclick(Random rng, int misclickPercent) =>
        rng.NextDouble() * 100.0 < Math.Clamp(misclickPercent, 0, 15);

    public static async Task BetweenActionsAsync(
        BehaviorRandomizerState state,
        AppSettings app,
        bool hardwareMode,
        IntPtr targetHwnd,
        Func<Point, Task> moveScreenAsync,
        Func<int, int, CancellationToken, Task> humanDelayAsync,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (!app.AntiDetectionEnabled || !app.AntiDetectionMicroPauseBehavior)
            return;

        try
        {
            state.ActionOrdinal++;
            state.StepsSinceLastMicroPause++;

            var rng = Random.Shared;

            if (app.AntiDetectionSessionBreakEnabled
                && !state.SessionLongBreakTaken
                && app.AntiDetectionSessionMinutes > 0)
            {
                double elapsedMin = (DateTimeOffset.UtcNow - state.SessionStartedUtc).TotalMinutes;
                if (elapsedMin >= app.AntiDetectionSessionMinutes)
                {
                    int breakMin = Math.Max(1, app.AntiDetectionSessionBreakMinMinutes);
                    int breakMax = Math.Max(breakMin, app.AntiDetectionSessionBreakMaxMinutes);
                    int pauseMin = rng.Next(breakMin, breakMax + 1);
                    log(string.Format(LanguageManager.GetString("ui_AntiLog_SessionBreak"), pauseMin));
                    state.SessionLongBreakTaken = true;
                    await humanDelayAsync(
                        (int)(pauseMin * 60_000),
                        Math.Max(5000, (int)(pauseMin * 60_000 / 8)),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            int nextPauseEvery = rng.Next(15, 31);
            if (state.StepsSinceLastMicroPause >= nextPauseEvery)
            {
                state.StepsSinceLastMicroPause = 0;
                int pauseMs = rng.Next(1500, 8001);
                log($"[Anti-Detection] Micro-pause {pauseMs}ms (suy nghĩ).");
                if (hardwareMode)
                {
                    Point cur = Win32MouseInput.GetCursorScreenPoint();
                    int ox = rng.Next(-14, 15);
                    int oy = rng.Next(-10, 11);
                    await moveScreenAsync(new Point(cur.X + ox, cur.Y + oy)).ConfigureAwait(false);
                }

                await humanDelayAsync(pauseMs, Math.Max(200, pauseMs / 4), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log($"[Anti-Detection] ⚠ BehaviorRandomizer: {ex.Message}");
        }
    }

    public static async Task MaybeScrollBeforeClickAsync(
        AppSettings app,
        bool hardwareMode,
        Random rng,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (!app.AntiDetectionEnabled || !hardwareMode)
            return;
        try
        {
            if (rng.NextDouble() > 0.40)
                return;
            int delta = rng.Next(-1, 2);
            if (delta == 0)
                delta = rng.Next(0, 2) == 0 ? -120 : 120;
            else
                delta *= 120;

            log(LanguageManager.GetString("ui_AntiLog_EyeTracking"));
            InputSpoofingService.SendMouseWheel(delta);
            await Task.Delay(rng.Next(40, 120), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log($"[Anti-Detection] ⚠ Scroll: {ex.Message}");
        }
    }
}
