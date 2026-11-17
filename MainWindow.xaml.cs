using System;
using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SkiaSharp.Views.Desktop;

namespace IslandWin;

public partial class MainWindow : Window
{
    readonly Droplet _sim = new();
    readonly LiquidRenderer _gfx = new();
    readonly DispatcherTimer _beep = new() { Interval = TimeSpan.FromMilliseconds(1600) };

    IntPtr _hwnd;
    double _dpi = 1.0;
    long _last = Stopwatch.GetTimestamp();
    bool _interactive, _wasAnimating = true, _alertOn;
    float _contentA, _pillA;
    int _page, _minute = -1;
    long _wheelMs;
    AlertInfo _alert;

    public MainWindow()
    {
        InitializeComponent();
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2.0;   // horizontally centred
        Top = 0;                                                       // glued to the screen edge

        ClockView.AlertRaised += ShowAlert;
        _beep.Tick += (_, _) => SystemSounds.Exclamation.Play();
        Closed += (_, _) => { CompositionTarget.Rendering -= Tick; _gfx.Dispose(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Native.Prepare(_hwnd);                       // tool window + click-through while idle
        CompositionTarget.Rendering += Tick;         // one loop: cursor polling + physics + draw
    }

    // ------------------------------------------------------------------ main loop
    void Tick(object sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        float dt = (float)((now - _last) / (double)Stopwatch.Frequency);
        _last = now;
        if (dt <= 0f) return;
        dt = Math.Min(dt, 0.05f);

        Native.GetCursorPos(out var p);
        float mx = (float)(p.X / _dpi - Left);
        float my = (float)(p.Y / _dpi - Top);

        _sim.Update(dt, mx, my, _alertOn);
        _sim.Step(dt);

        // Only catch the mouse while it is over the visible island; otherwise clicks pass through.
        bool live = (_sim.Phase == Phase.Pill || _sim.Phase == Phase.Expanded) && _sim.Contains(mx, my, 6f);
        if (live != _interactive) { _interactive = live; Native.ClickThrough(_hwnd, !live); }

        UpdateOverlays(dt);

        bool animating = !_sim.Settled;
        if (animating || _wasAnimating) Goo.InvalidateVisual();   // idle = zero repaints
        _wasAnimating = animating;
    }

    void UpdateOverlays(float dt)
    {
        bool expanded = _sim.Phase == Phase.Expanded;
        float prog = Math.Clamp((_sim.Wd.V - Droplet.PillW) / (Droplet.PanelW - Droplet.PillW), 0f, 1f);
        float contentTarget = expanded ? Droplet.Smooth(0.75f, 0.97f, prog) : 0f;
        float pillTarget = _sim.Phase == Phase.Pill
            ? Droplet.Smooth(0.55f, 0.90f, Math.Clamp(_sim.Wd.V / Droplet.PillW, 0f, 1f)) : 0f;

        float k = Math.Min(1f, dt * 16f);
        _contentA = Approach(_contentA, contentTarget, k);
        _pillA = Approach(_pillA, pillTarget, k);

        bool showPanel = _contentA > 0.01f, showPill = _pillA > 0.01f;
        SetVisible(Panel, showPanel);
        SetVisible(PillText, showPill);

        if (showPanel)
        {
            if (Math.Abs(Panel.Opacity - _contentA) > 0.002) Panel.Opacity = _contentA;
            Panel.IsHitTestVisible = _contentA > 0.8f;
            SetShift(PanelShift, _sim.Y.V - (Droplet.PanelTop + Droplet.PanelH * 0.5f));
        }
        if (showPill)
        {
            var t = DateTime.Now;
            if (t.Minute != _minute) { _minute = t.Minute; PillTime.Text = t.ToString("h:mm"); PillAmPm.Text = t.ToString("tt"); }
            if (Math.Abs(PillText.Opacity - _pillA) > 0.002) PillText.Opacity = _pillA;
            SetShift(PillShift, _sim.Y.V - Droplet.PillY);
        }
    }

    static float Approach(float cur, float target, float k)
    {
        float v = cur + (target - cur) * k;
        return Math.Abs(target - v) < 0.003f ? target : v;
    }
    static void SetShift(TranslateTransform tt, float y) { if (Math.Abs(tt.Y - y) > 0.01) tt.Y = y; }
    static void SetVisible(UIElement el, bool on)
    {
        var v = on ? Visibility.Visible : Visibility.Collapsed;
        if (el.Visibility != v) el.Visibility = v;
    }

    void OnPaint(object sender, SKPaintSurfaceEventArgs e)
    {
        double w = Goo.ActualWidth;
        if (w <= 0) return;
        _gfx.Render(e.Surface.Canvas, e.RawInfo, (float)(e.RawInfo.Width / w), _sim);
    }

    // ------------------------------------------------------------------ input
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_sim.Phase == Phase.Pill) _sim.Expand();       // click the pill to open it
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (_sim.Phase != Phase.Expanded || _alertOn) return;
        long now = Environment.TickCount64;
        if (now - _wheelMs > 320) { GoPage(_page + (e.Delta < 0 ? 1 : -1)); _wheelMs = now; }
        e.Handled = true;                                   // scroll = switch page
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !_alertOn) _sim.Collapse();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (!_alertOn) _sim.Collapse();                     // clicked somewhere else
    }

    void GoPage(int index)
    {
        index = Math.Clamp(index, 0, 1);
        if (index == _page) return;
        _page = index;
        PageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(
            -index * Droplet.PanelH, TimeSpan.FromMilliseconds(380))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        Dot0.Opacity = index == 0 ? 1.0 : 0.3;
        Dot1.Opacity = index == 1 ? 1.0 : 0.3;
    }

    // ------------------------------------------------------------------ alerts
    void ShowAlert(AlertInfo a)
    {
        _alert = a;
        _alertOn = true;
        AlertTitle.Text = a.Title;
        AlertSub.Text = a.Subtitle;
        SnoozeBtn.Visibility = a.Snooze != null ? Visibility.Visible : Visibility.Collapsed;
        AlertLayer.Visibility = Visibility.Visible;
        _sim.ForceExpand();                                  // the drop falls by itself
        SystemSounds.Exclamation.Play();
        _beep.Start();
    }

    void CloseAlert()
    {
        _beep.Stop();
        _alertOn = false;
        AlertLayer.Visibility = Visibility.Collapsed;
        _sim.Collapse();
    }

    void OnSnooze(object s, RoutedEventArgs e) { _alert?.Snooze?.Invoke(); CloseAlert(); }
    void OnDismiss(object s, RoutedEventArgs e) => CloseAlert();
}
