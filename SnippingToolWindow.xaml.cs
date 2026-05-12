using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace SmartMacroAI;

/// <summary>
/// PMC-style screen capture tool with realtime info panel:
/// cursor position, selection size, pixel color, magnifier zoom.
/// Created by Phạm Duy – Giải pháp tự động hóa thông minh.
/// </summary>
public partial class SnippingToolWindow : Window
{
    private System.Windows.Point _startPoint;
    private bool _isDragging;
    private Bitmap? _fullScreenshot;

    public string? CapturedFilePath { get; private set; }

    /// <summary>Screen-space rectangle of the last successful selection.</summary>
    public System.Drawing.Rectangle SelectedScreenRectangle { get; private set; }

    public static string TemplatesFolder
    {
        get
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public SnippingToolWindow()
    {
        InitializeComponent();
        CaptureFullScreen();
    }

    private void CaptureFullScreen()
    {
        int w = (int)SystemParameters.VirtualScreenWidth;
        int h = (int)SystemParameters.VirtualScreenHeight;
        int x = (int)SystemParameters.VirtualScreenLeft;
        int y = (int)SystemParameters.VirtualScreenTop;

        _fullScreenshot = new Bitmap(w, h, DrawingPixelFormat.Format32bppArgb);
        using var gfx = Graphics.FromImage(_fullScreenshot);
        gfx.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));

        using var ms = new MemoryStream();
        _fullScreenshot.Save(ms, ImageFormat.Png);
        ms.Position = 0;

        var bi = new BitmapImage();
        bi.BeginInit();
        bi.StreamSource = ms;
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();

        ScreenImage.Source = bi;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(OverlayCanvas);
        _isDragging = true;

        Canvas.SetLeft(SelectionRect, _startPoint.X);
        Canvas.SetTop(SelectionRect, _startPoint.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionRect.Visibility = Visibility.Visible;

        OverlayCanvas.CaptureMouse();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(OverlayCanvas);
        int cx = (int)current.X;
        int cy = (int)current.Y;

        // Update cursor position
        TxtCursorPos.Text = $"{cx}, {cy}";

        // Update pixel color under cursor
        UpdatePixelInfo(cx, cy);

        // Update magnifier position and content
        UpdateMagnifier(cx, cy);

        // Update selection rectangle if dragging
        if (!_isDragging) return;

        double x = Math.Min(_startPoint.X, current.X);
        double y = Math.Min(_startPoint.Y, current.Y);
        double w = Math.Abs(current.X - _startPoint.X);
        double h = Math.Abs(current.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;

        TxtSelectionSize.Text = $"{(int)w} × {(int)h} px";
    }

    private void UpdatePixelInfo(int x, int y)
    {
        if (_fullScreenshot == null) return;

        int bmpX = x - (int)SystemParameters.VirtualScreenLeft;
        int bmpY = y - (int)SystemParameters.VirtualScreenTop;

        if (bmpX < 0 || bmpY < 0 || bmpX >= _fullScreenshot.Width || bmpY >= _fullScreenshot.Height)
            return;

        try
        {
            var pixel = _fullScreenshot.GetPixel(bmpX, bmpY);
            TxtPixelColor.Text = $"#{pixel.R:X2}{pixel.G:X2}{pixel.B:X2}";
            TxtPixelRgb.Text = $"{pixel.R}, {pixel.G}, {pixel.B}";
            ColorPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(pixel.R, pixel.G, pixel.B));
        }
        catch { }
    }

    private void UpdateMagnifier(int screenX, int screenY)
    {
        if (_fullScreenshot == null) return;

        // Position magnifier near cursor (offset to avoid overlap)
        double magX = screenX + 20;
        double magY = screenY - 140;
        if (magY < 0) magY = screenY + 20;
        if (magX + 130 > ActualWidth) magX = screenX - 140;

        Canvas.SetLeft(MagnifierBorder, magX);
        Canvas.SetTop(MagnifierBorder, magY);

        // Extract 15x15 pixel region around cursor and display zoomed
        int bmpX = screenX - (int)SystemParameters.VirtualScreenLeft;
        int bmpY = screenY - (int)SystemParameters.VirtualScreenTop;
        int halfSize = 7;

        int srcX = Math.Clamp(bmpX - halfSize, 0, _fullScreenshot.Width - 1);
        int srcY = Math.Clamp(bmpY - halfSize, 0, _fullScreenshot.Height - 1);
        int srcW = Math.Min(halfSize * 2 + 1, _fullScreenshot.Width - srcX);
        int srcH = Math.Min(halfSize * 2 + 1, _fullScreenshot.Height - srcY);

        if (srcW <= 0 || srcH <= 0) return;

        try
        {
            using var region = _fullScreenshot.Clone(
                new Rectangle(srcX, srcY, srcW, srcH), _fullScreenshot.PixelFormat);

            using var ms = new MemoryStream();
            region.Save(ms, ImageFormat.Bmp);
            ms.Position = 0;

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = ms;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();

            MagnifierImage.Source = bi;
        }
        catch { }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        OverlayCanvas.ReleaseMouseCapture();

        int x = (int)Canvas.GetLeft(SelectionRect);
        int y = (int)Canvas.GetTop(SelectionRect);
        int w = (int)SelectionRect.Width;
        int h = (int)SelectionRect.Height;

        if (w < 5 || h < 5 || _fullScreenshot is null)
        {
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = false;
            Close();
            return;
        }

        int offsetX = (int)SystemParameters.VirtualScreenLeft;
        int offsetY = (int)SystemParameters.VirtualScreenTop;
        x += offsetX;
        y += offsetY;

        try
        {
            using var cropped = _fullScreenshot.Clone(
                new Rectangle(x - offsetX, y - offsetY, w, h), _fullScreenshot.PixelFormat);

            string fileName = $"snip_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            CapturedFilePath = Path.Combine(TemplatesFolder, fileName);
            cropped.Save(CapturedFilePath, ImageFormat.Png);

            SelectedScreenRectangle = new System.Drawing.Rectangle(x, y, w, h);
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true;
        }
        catch
        {
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = false;
        }

        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = false;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _fullScreenshot?.Dispose();
        base.OnClosed(e);
    }
}
