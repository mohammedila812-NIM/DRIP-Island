using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace IslandWin;

/// <summary>
/// Now-playing page. Reads whatever app owns the Windows media session
/// (Spotify, Apple Music, browsers, VLC, ...) through the system transport controls.
/// </summary>
public partial class MusicPage : UserControl
{
    GlobalSystemMediaTransportControlsSessionManager _mgr;
    GlobalSystemMediaTransportControlsSession _s;
    bool _playing;
    TimeSpan _start, _end, _pos;
    DateTimeOffset _stamp = DateTimeOffset.Now;
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public MusicPage()
    {
        InitializeComponent();
        _t.Tick += (_, _) => Progress();
        _t.Start();
        Loaded += async (_, _) => await Init();
    }

    async Task Init()
    {
        if (_mgr != null) return;
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => Dispatcher.BeginInvoke(new Action(Attach));
            Attach();
        }
        catch (Exception ex)
        {
            Log.Error("MusicPage.Init", ex);
            Clear();
        }
    }

    void Attach()
    {
        if (_s != null)
        {
            _s.MediaPropertiesChanged -= OnProps;
            _s.PlaybackInfoChanged -= OnPlayback;
            _s.TimelinePropertiesChanged -= OnTimeline;
        }
        _s = _mgr?.GetCurrentSession();
        if (_s == null) { Clear(); return; }

        _s.MediaPropertiesChanged += OnProps;
        _s.PlaybackInfoChanged += OnPlayback;
        _s.TimelinePropertiesChanged += OnTimeline;
        _ = RefreshProps(); RefreshPlayback(); RefreshTimeline();
    }

    // these events arrive on a worker thread
    void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => { _ = RefreshProps(); }));
    void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(RefreshPlayback));
    void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(RefreshTimeline));

    void Clear()
    {
        _s = null; _playing = false;
        TitleText.Text = "Nothing playing";
        ArtistText.Text = "Play something to see it here";
        Art.Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E));
        PlayBtn.Content = "\uE768";
        Fill.Width = 0; PosText.Text = "0:00"; DurText.Text = "0:00";
    }

    async Task RefreshProps()
    {
        var s = _s;
        if (s == null) return;
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            TitleText.Text = string.IsNullOrWhiteSpace(props.Title) ? "Unknown title" : props.Title;
            ArtistText.Text = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumTitle : props.Artist;
            await LoadArt(props.Thumbnail);
        }
        catch (Exception ex)
        {
            Log.Error("MusicPage.RefreshProps", ex);
        }
    }

    async Task LoadArt(IRandomAccessStreamReference thumb)
    {
        Brush fallback = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E));
        if (thumb == null) { Art.Background = fallback; return; }
        try
        {
            using var rs = await thumb.OpenReadAsync();
            using var ms = new MemoryStream();
            await rs.AsStreamForRead().CopyToAsync(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 256;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            Art.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex)
        {
            Log.Error("MusicPage.LoadArt", ex);
            Art.Background = fallback;
        }
    }

    void RefreshPlayback()
    {
        var info = _s?.GetPlaybackInfo();
        _playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        PlayBtn.Content = _playing ? "\uE769" : "\uE768";
    }

    void RefreshTimeline()
    {
        var tl = _s?.GetTimelineProperties();
        if (tl == null) return;
        _start = tl.StartTime; _end = tl.EndTime; _pos = tl.Position; _stamp = tl.LastUpdatedTime;
    }

    static string Mmss(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    void Progress()
    {
        if (!IsVisible || _s == null) return;
        var total = _end - _start;
        if (total <= TimeSpan.Zero) { Fill.Width = 0; return; }

        var pos = _pos - _start + (_playing ? DateTimeOffset.Now - _stamp : TimeSpan.Zero);
        pos = pos < TimeSpan.Zero ? TimeSpan.Zero : (pos > total ? total : pos);

        Fill.Width = Track.ActualWidth * (pos.TotalSeconds / total.TotalSeconds);
        PosText.Text = Mmss(pos);
        DurText.Text = Mmss(total);
    }

    async void OnPlayPause(object s, RoutedEventArgs e)
    {
        try { if (_s != null) await _s.TryTogglePlayPauseAsync(); }
        catch (Exception ex) { Log.Error("MusicPage.OnPlayPause", ex); }
    }

    async void OnNext(object s, RoutedEventArgs e)
    {
        try { if (_s != null) await _s.TrySkipNextAsync(); }
        catch (Exception ex) { Log.Error("MusicPage.OnNext", ex); }
    }

    async void OnPrev(object s, RoutedEventArgs e)
    {
        try { if (_s != null) await _s.TrySkipPreviousAsync(); }
        catch (Exception ex) { Log.Error("MusicPage.OnPrev", ex); }
    }

    async void OnSeek(object s, MouseButtonEventArgs e)
    {
        if (_s == null || Track.ActualWidth <= 0) return;
        var total = _end - _start;
        if (total <= TimeSpan.Zero) return;
        double f = Math.Clamp(e.GetPosition(Track).X / Track.ActualWidth, 0.0, 1.0);
        try
        {
            await _s.TryChangePlaybackPositionAsync((_start + TimeSpan.FromSeconds(total.TotalSeconds * f)).Ticks);
        }
        catch (Exception ex)
        {
            Log.Error("MusicPage.OnSeek", ex);
        }
    }
}
