using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace SmartMacroAI;

public partial class SnippingToolWindow : Window
{
    private System.Windows.Point _startPoint;
    private bool _isDragging;
    private Bitmap? _fullScreenshot;

    public string? CapturedFilePath { get; private set; }

    /// <summary>Screen-space rectangle of the last successful selection (valid when <see cref="DialogResult"/> is true).</summary>
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

        _fullScreenshot = new Bitmap(w, h, PixelFormat.Format32bppArgb);
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
        if (!_isDragging) return;

        var current = e.GetPosition(OverlayCanvas);

        double x = Math.Min(_startPoint.X, current.X);
        double y = Math.Min(_startPoint.Y, current.Y);
        double w = Math.Abs(current.X - _startPoint.X);
        double h = Math.Abs(current.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
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
