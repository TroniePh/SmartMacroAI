using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SmartMacroAI;

public partial class CoordinatePickerWindow : Window
{
    private readonly IntPtr _targetHwnd;
    private readonly Rectangle _coordBoxBg;
    private readonly System.Windows.Controls.TextBlock _coordLabel;
    private readonly Line _crosshairLineH;
    private readonly Line _crosshairLineV;

    public System.Drawing.Point PickedPoint { get; private set; }
    public int PickedMonitorIndex { get; private set; } = 0;

    public CoordinatePickerWindow() : this(IntPtr.Zero) { }

    public CoordinatePickerWindow(IntPtr targetHwnd)
    {
        _targetHwnd = targetHwnd;
        InitializeComponent();

        var darkBrush = new SolidColorBrush(Color.FromArgb(200, 30, 30, 46));
        var accentBrush = new SolidColorBrush(Color.FromRgb(137, 180, 250));
        var accentSemi = new SolidColorBrush(Color.FromArgb(180, 137, 180, 250));

        _crosshairLineH = new Line
        {
            Stroke = accentSemi,
            StrokeThickness = 1,
            IsHitTestVisible = false,
            X1 = 0,
        };
        _crosshairLineV = new Line
        {
            Stroke = accentSemi,
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Y1 = 0,
        };
        _coordBoxBg = new Rectangle
        {
            Fill = darkBrush,
            RadiusX = 6,
            RadiusY = 6,
            IsHitTestVisible = false,
        };
        _coordLabel = new System.Windows.Controls.TextBlock
        {
            Foreground = accentBrush,
            FontSize = 14,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false,
            Margin = new Thickness(10, 6, 10, 6),
        };

        GuideCanvas.Children.Add(_crosshairLineH);
        GuideCanvas.Children.Add(_crosshairLineV);
        GuideCanvas.Children.Add(_coordBoxBg);
        GuideCanvas.Children.Add(_coordLabel);

        MouseMove += CoordinatePickerWindow_MouseMove;
    }

    private void CoordinatePickerWindow_MouseMove(object sender, MouseEventArgs e)
    {
        var screenPos = GetCursorPosOnScreen();
        PickedPoint = screenPos;

        _crosshairLineH.Y1 = screenPos.Y;
        _crosshairLineH.Y2 = screenPos.Y;
        _crosshairLineH.X2 = ActualWidth;

        _crosshairLineV.X1 = screenPos.X;
        _crosshairLineV.X2 = screenPos.X;
        _crosshairLineV.Y2 = ActualHeight;

        double labelX = screenPos.X + 15;
        double labelY = screenPos.Y - _coordBoxBg.Height - 10;
        if (labelX + _coordBoxBg.Width > ActualWidth)
            labelX = screenPos.X - _coordBoxBg.Width - 15;
        if (labelY < 0)
            labelY = screenPos.Y + 20;

        System.Windows.Controls.Canvas.SetLeft(_coordBoxBg, labelX);
        System.Windows.Controls.Canvas.SetTop(_coordBoxBg, labelY);
        System.Windows.Controls.Canvas.SetLeft(_coordLabel, labelX);
        System.Windows.Controls.Canvas.SetTop(_coordLabel, labelY);

        string display = _targetHwnd != IntPtr.Zero
            ? $"S: {screenPos.X},{screenPos.Y}  W: {screenPos.X},{screenPos.Y}"
            : $"X: {screenPos.X}  Y: {screenPos.Y}";
        _coordLabel.Text = display;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var screen = GetCursorPosOnScreen();
        PickedPoint = screen;

        if (_targetHwnd != IntPtr.Zero)
        {
            POINT pt = new POINT { X = screen.X, Y = screen.Y };
            if (ScreenToClient(_targetHwnd, ref pt))
                PickedPoint = new System.Drawing.Point(pt.X, pt.Y);
        }

        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true; else Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = false; else Close();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private static System.Drawing.Point GetCursorPosOnScreen()
    {
        GetCursorPos(out var p);
        return new System.Drawing.Point(p.X, p.Y);
    }
}
