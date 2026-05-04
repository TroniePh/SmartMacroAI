using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using SmartMacroAI.Localization;
using TesseractOcr = Tesseract;

namespace SmartMacroAI.Core;

/// <summary>
/// Computer-vision layer for SmartMacroAI.
/// All capture operations use <see cref="Win32Api.CaptureHiddenWindow"/> (PrintWindow + BitBlt fallback),
/// which works on background / occluded / minimized windows without bringing them
/// to the foreground.
///
/// • Template matching   → Emgu.CV multi-scale <c>CcoeffNormed</c>
/// • Text recognition    → Tesseract OCR
/// </summary>
public static class VisionEngine
{
    private static readonly object TessLock = new();

    public static string TessDataPath { get; set; } =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");

    public static string TessLanguage { get; set; } = "eng";

    private static readonly Dictionary<string, TesseractOcr.TesseractEngine> _engineCache = new();

    // ═══════════════════════════════════════════════════
    //  BITMAP ↔ MAT CONVERSION
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Converts a <see cref="Bitmap"/> to an Emgu.CV <see cref="Mat"/>
    /// by encoding to PNG in memory and decoding with OpenCV.
    /// Works with any Emgu.CV version — no extension-method dependency.
    /// </summary>
    private static Mat BitmapToMat(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Bmp);
        byte[] bytes = ms.ToArray();
        var mat = new Mat();
        CvInvoke.Imdecode(bytes, ImreadModes.ColorBgr, mat);
        return mat;
    }

    // ═══════════════════════════════════════════════════
    //  BACKGROUND WINDOW CAPTURE
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Captures a background window client area into a <see cref="Bitmap"/> via
    /// <see cref="Win32Api.CaptureHiddenWindow"/>.
    /// </summary>
    public static Bitmap CaptureHiddenWindow(IntPtr hwnd)
    {
        Bitmap? bmp = Win32Api.CaptureHiddenWindow(hwnd);
        if (bmp is null)
            throw new InvalidOperationException(
                $"Failed to capture window (HWND=0x{hwnd:X}). " +
                "The handle may be invalid or the window may have zero size.");
        return bmp;
    }

    // ═══════════════════════════════════════════════════
    //  TEMPLATE MATCHING  (Emgu.CV / OpenCV, multi-scale)
    // ═══════════════════════════════════════════════════

    private static readonly double[] DefaultMultiScales = [0.80, 0.90, 1.00, 1.10, 1.25];

    private static double[] BuildScalesFromSettings()
    {
        var s = AppSettings.Load();
        double min = Math.Clamp(s.VisionMatchMinScale, 0.15, 4.0);
        double max = Math.Clamp(s.VisionMatchMaxScale, 0.15, 4.0);
        if (min > max)
            (min, max) = (max, min);

        const int steps = 7;
        var arr = new double[steps];
        for (int i = 0; i < steps; i++)
            arr[i] = min + (max - min) * i / (steps - 1);
        return arr;
    }

    /// <summary>
    /// Best match across <paramref name="scales"/> on <paramref name="searchRegion"/> (optional ROI).
    /// Returns center in full <paramref name="sourceMatFull"/> client coordinates, confidence, scale, and the effective scanned rectangle (empty = full frame).
    /// </summary>
    private static (Point Center, double Confidence, double Scale, Rectangle ScannedRegion)?
        MatchTemplateMultiScaleCore(
            Mat sourceMatFull,
            Mat templateMat,
            double[] scales,
            Rectangle? searchRegion)
    {
        if (templateMat.IsEmpty)
            return null;

        Mat workingSource = sourceMatFull;
        var effectiveRoi = Rectangle.Empty;
        bool disposeWorkingSlice = false;

        if (searchRegion.HasValue)
        {
            var roi = searchRegion.Value;
            roi.Intersect(new Rectangle(0, 0, sourceMatFull.Width, sourceMatFull.Height));
            if (roi.Width > 0 && roi.Height > 0)
            {
                workingSource = new Mat(sourceMatFull, roi);
                effectiveRoi = roi;
                disposeWorkingSlice = true;
            }
        }

        try
        {
            double bestConfidence = -1;
            var bestCenter = Point.Empty;
            double bestScale = 1.0;
            var ranAny = false;

            foreach (double scale in scales)
            {
                int newW = (int)(templateMat.Width * scale);
                int newH = (int)(templateMat.Height * scale);

                if (newW <= 0 || newH <= 0)
                    continue;
                if (newW > workingSource.Width || newH > workingSource.Height)
                    continue;

                ranAny = true;
                using var scaledTemplate = new Mat();
                CvInvoke.Resize(templateMat, scaledTemplate, new Size(newW, newH), 0, 0, Inter.Linear);

                using var result = new Mat();
                CvInvoke.MatchTemplate(workingSource, scaledTemplate, result, TemplateMatchingType.CcoeffNormed);

                double minVal = 0, maxVal = 0;
                Point minLoc = default, maxLoc = default;
                CvInvoke.MinMaxLoc(result, ref minVal, ref maxVal, ref minLoc, ref maxLoc);

                if (maxVal > bestConfidence)
                {
                    bestConfidence = maxVal;
                    bestScale = scale;
                    bestCenter = new Point(
                        maxLoc.X + newW / 2,
                        maxLoc.Y + newH / 2);
                }
            }

            if (!ranAny || bestConfidence < 0)
                return null;

            if (!effectiveRoi.IsEmpty)
            {
                bestCenter.X += effectiveRoi.X;
                bestCenter.Y += effectiveRoi.Y;
            }

            return (bestCenter, bestConfidence, bestScale, effectiveRoi);
        }
        finally
        {
            if (disposeWorkingSlice)
                workingSource.Dispose();
        }
    }

    private static void LogVisionMatchResult(
        string status,
        double bestConfidence,
        double bestScale,
        Point bestCenter,
        Rectangle scannedRegion)
    {
        string roiPart = scannedRegion.IsEmpty ? "Full window" : scannedRegion.ToString();
        Debug.WriteLine(
            $"[Vision] {status} | Conf: {bestConfidence * 100:F1}% " +
            $"| Scale: {bestScale:F2}x " +
            $"| Center: ({bestCenter.X},{bestCenter.Y}) " +
            $"| ROI: {roiPart}");
    }

    /// <summary>
    /// Multi-scale template match on a captured bitmap. <paramref name="scales"/> defaults to
    /// <see cref="DefaultMultiScales"/> when null.
    /// </summary>
    public static Point? FindImageInBitmapMultiScale(
        Bitmap source,
        string templatePath,
        double threshold = 0.8,
        double[]? scales = null,
        Rectangle? searchRegion = null)
    {
        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Template image not found.", templatePath);

        scales ??= DefaultMultiScales;

        using Mat sourceMat = BitmapToMat(source);
        using Mat templateMat = CvInvoke.Imread(templatePath, ImreadModes.ColorBgr);

        var best = MatchTemplateMultiScaleCore(sourceMat, templateMat, scales, searchRegion);
        if (best is null)
            return null;

        var (center, conf, scaleUsed, scanned) = best.Value;
        string status = conf >= threshold ? "FOUND" : "NOT FOUND";
        LogVisionMatchResult(status, conf, scaleUsed, center, scanned);

        return conf >= threshold ? center : null;
    }

    /// <summary>
    /// Captures the target window and runs multi-scale template matching (DPI-aware).
    /// </summary>
    public static Point? FindImageOnWindowMultiScale(
        IntPtr hwnd,
        string templatePath,
        double threshold = 0.8,
        double[]? scales = null,
        Rectangle? searchRegion = null)
    {
        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Template image not found.", templatePath);

        scales ??= BuildScalesFromSettings();

        using Bitmap captured = CaptureHiddenWindow(hwnd);
        return FindImageInBitmapMultiScale(captured, templatePath, threshold, scales, searchRegion);
    }

    /// <summary>
    /// Single-scale (1.0) fallback — delegates to <see cref="FindImageOnWindowMultiScale"/>.
    /// </summary>
    public static Point? FindImageOnWindow(IntPtr hwnd, string templatePath, double threshold = 0.8)
        => FindImageOnWindowMultiScale(hwnd, templatePath, threshold, new[] { 1.0 }, null);

    /// <summary>
    /// Single-scale (1.0) fallback on a pre-captured bitmap.
    /// </summary>
    public static Point? FindImageInBitmap(Bitmap source, string templatePath, double threshold = 0.8)
        => FindImageInBitmapMultiScale(source, templatePath, threshold, new[] { 1.0 }, null);

    /// <summary>
    /// Multi-scale match with best confidence and scale for UI / diagnostics.
    /// </summary>
    public static (Point Location, double Confidence, double Scale, Rectangle ScannedRegion)?
        FindImageOnWindowDetailed(IntPtr hwnd, string templatePath, Rectangle? searchRegion = null)
    {
        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Template image not found.", templatePath);

        using Bitmap captured = CaptureHiddenWindow(hwnd);
        using Mat sourceMat = BitmapToMat(captured);
        using Mat templateMat = CvInvoke.Imread(templatePath, ImreadModes.ColorBgr);

        var best = MatchTemplateMultiScaleCore(sourceMat, templateMat, BuildScalesFromSettings(), searchRegion);
        if (best is null)
            return null;

        var (center, conf, scale, scanned) = best.Value;
        LogVisionMatchResult("BEST", conf, scale, center, scanned);

        return (center, conf, scale, scanned);
    }

    // ═══════════════════════════════════════════════════
    //  OCR  (Tesseract)
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Captures the target window in the background and runs Tesseract OCR
    /// to extract all visible text.
    /// Requires tessdata/{lang}.traineddata to be present.
    /// </summary>
    public static string ExtractTextFromWindow(IntPtr hwnd, string language = "eng")
    {
        using Bitmap captured = CaptureHiddenWindow(hwnd);
        return ExtractTextFromBitmap(captured, language);
    }

    /// <summary>
    /// Runs Tesseract OCR on a pre-captured bitmap using the specified language.
    /// Supported: "eng", "vie", or "eng+vie".
    /// </summary>
    public static string ExtractTextFromBitmap(Bitmap bitmap, string language = "eng")
    {
        try
        {
            var engine = GetTesseractEngine(language);
            if (engine is null)
                return $"[OCR unavailable — tessdata not found. " +
                       $"Place {language}.traineddata in: {TessDataPath}]";

            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            using var pix  = TesseractOcr.Pix.LoadFromMemory(ms.ToArray());
            using var page = engine.Process(pix);
            return page.GetText().Trim();
        }
        catch (Exception ex)
        {
            string realError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                System.Windows.MessageBox.Show(
                    $"{LanguageManager.GetString("ui_Msg_OcrError")}\n\n{realError}\n\n{ex.GetType().Name}",
                    "OCR Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            });
            return $"[OCR error: {realError}]";
        }
    }

    /// <summary>
    /// Checks whether the required tessdata files exist for the given language.
    /// </summary>
    public static bool IsTesseractAvailable(string language = "eng")
    {
        string trainedDataFile = Path.Combine(TessDataPath, $"{language}.traineddata");
        if (File.Exists(trainedDataFile)) return true;
        if (language.Contains('+'))
        {
            foreach (var lang in language.Split('+'))
            {
                if (!File.Exists(Path.Combine(TessDataPath, $"{lang.Trim()}.traineddata")))
                    return false;
            }
            return true;
        }
        return false;
    }

    private static TesseractOcr.TesseractEngine? GetTesseractEngine(string language = "eng")
    {
        if (_engineCache.TryGetValue(language, out var cached))
            return cached;

        lock (TessLock)
        {
            if (_engineCache.TryGetValue(language, out cached))
                return cached;

            if (!IsTesseractAvailable(language))
            {
                System.Diagnostics.Debug.WriteLine($"[VisionEngine] tessdata not found for language: {language}");
                return null;
            }

            try
            {
                var engine = new TesseractOcr.TesseractEngine(
                    TessDataPath,
                    language,
                    TesseractOcr.EngineMode.Default);
                _engineCache[language] = engine;
                return engine;
            }
            catch (Exception ex)
            {
                string realError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                System.Diagnostics.Debug.WriteLine($"[VisionEngine] TesseractEngine init failed ({language}): {realError}");
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show(
                        $"{LanguageManager.GetString("ui_Msg_OcrError")}\n\n{realError}\n\n{ex.GetType().Name}\n\n{string.Format(LanguageManager.GetString("ui_Msg_OcrInitHint"), language)}",
                        "OCR Init Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                });
                return null;
            }
        }
    }

    /// <summary>
    /// Extracts text and simultaneously saves it to a timestamped .txt file,
    /// then opens the containing folder in Explorer.
    /// </summary>
    public static string ExtractTextAndSave(Bitmap bitmap, string language = "eng")
    {
        string text = ExtractTextFromBitmap(bitmap, language);

        if (string.IsNullOrWhiteSpace(text) || text.StartsWith("[OCR error") || text.StartsWith("[OCR unavailable"))
            return text;

        string extractDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "extracts");

        Directory.CreateDirectory(extractDir);

        string fileName = $"ocr_{DateTime.Now:yyyyMMdd_HHmmss}_{language}.txt";
        string filePath = Path.Combine(extractDir, fileName);

        File.WriteAllText(filePath, text, System.Text.Encoding.UTF8);

        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\""); }
        catch { /* ignore if explorer fails */ }

        return text;
    }

    /// <summary>
    /// Extracts text from a window (captures + OCR + saves to file + opens folder).
    /// </summary>
    public static string ExtractTextAndSave(IntPtr hwnd, string language = "eng")
    {
        using Bitmap captured = CaptureHiddenWindow(hwnd);
        return ExtractTextAndSave(captured, language);
    }

    /// <summary>
    /// Releases all cached Tesseract engines (call on app shutdown).
    /// </summary>
    public static void Shutdown()
    {
        lock (TessLock)
        {
            foreach (var engine in _engineCache.Values)
                engine?.Dispose();
            _engineCache.Clear();
        }
    }
}
