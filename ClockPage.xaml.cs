using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace IslandWin;

/// <summary>Clock suite: clock, stopwatch, timer, alarm, reminder.</summary>
public partial class ClockPage : UserControl
{
    public event Action<AlertInfo> AlertRaised;

    static readonly Brush Green = Hex("#30D158"), Orange = Hex("#FF9F0A");
    static Brush Hex(string c) => (Brush)new BrushConverter().ConvertFromString(c);

    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly StoreData _data = Store.Load();
    readonly FrameworkElement[] _tabs;
    int _lastSec = -1;

    // stopwatch
    readonly Stopwatch _sw = new();
    TimeSpan _lapMark;
    int _lapCount;

    // timer
    TimeSpan _timerSet = TimeSpan.FromMinutes(5), _timerLeft = TimeSpan.FromMinutes(5);
    DateTime _timerEnd;
    bool _timerRun;

    // alarm editor (12 hour)
    int _ah = 7, _am = 0;
    bool _pm;

    // reminder editor
    int _remMin = 15;

    public ClockPage()
    {
        InitializeComponent();
        _tabs = new FrameworkElement[] { TabClock, TabStop, TabTimer, TabAlarm, TabReminder };

        ((RadioButton)Tabs.Children[0]).IsChecked = true;
        Rm15.IsChecked = true;

        UpdateSwUi(); UpdateTimerUi(); UpdateAlarmEditor(); RefreshAlarmUi(); RefreshReminderUi();

        _tick.Tick += (_, _) => OnTick();
        _tick.Start();
    }

    void Raise(AlertInfo a) => AlertRaised?.Invoke(a);
    void Save() => Store.Save(_data);

    void OnTab(object sender, RoutedEventArgs e)
    {
        if (_tabs == null) return;
        int i = int.Parse((string)((RadioButton)sender).Tag);
        for (int n = 0; n < _tabs.Length; n++)
            _tabs[n].Visibility = n == i ? Visibility.Visible : Visibility.Collapsed;
        if (i == 0) EverySecond(DateTime.Now);
    }

    // ------------------------------------------------------------------ ticking
    void OnTick()
    {
        var now = DateTime.Now;
        if (now.Second != _lastSec) { _lastSec = now.Second; EverySecond(now); }
        if (_sw.IsRunning && TabStop.IsVisible) UpdateSwUi();
        if (_timerRun) TickTimer();
    }

    void EverySecond(DateTime now)
    {
        if (TabClock.IsVisible)
        {
            ClkMain.Text = now.ToString("h:mm");
            ClkSec.Text = now.ToString(":ss tt");
            ClkDate.Text = now.ToString("dddd, d MMMM");
        }

        for (int i = _data.Alarms.Count - 1; i >= 0; i--)
        {
            var a = _data.Alarms[i];
            if (a.Hour * 60 + a.Minute == now.Hour * 60 + now.Minute && a.LastFired.Date != now.Date)
            {
                _data.Alarms.RemoveAt(i);
                Save(); RefreshAlarmUi();
                Raise(new AlertInfo("Alarm", a.Label, () => AddAlarmAt(DateTime.Now.AddMinutes(5))));
            }
        }

        for (int i = _data.Reminders.Count - 1; i >= 0; i--)
        {
            var r = _data.Reminders[i];
            if (r.Due <= now)
            {
                _data.Reminders.RemoveAt(i);
                Save(); RefreshReminderUi();
                Raise(new AlertInfo("Reminder", r.Text, null));
            }
        }
    }

    // ------------------------------------------------------------------ stopwatch
    static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}";

    void UpdateSwUi()
    {
        SwText.Text = Fmt(_sw.Elapsed);
        SwStartBtn.Content = _sw.IsRunning ? "Stop" : "Start";
        SwStartBtn.Background = _sw.IsRunning ? Hex("#FF453A") : Green;
        SwLapBtn.Content = _sw.IsRunning ? "Lap" : "Reset";
    }

    void OnSwStart(object s, RoutedEventArgs e)
    {
        if (_sw.IsRunning) _sw.Stop(); else _sw.Start();
        UpdateSwUi();
    }

    void OnSwLap(object s, RoutedEventArgs e)
    {
        if (_sw.IsRunning)
        {
            var lap = _sw.Elapsed - _lapMark;
            _lapMark = _sw.Elapsed;
            _lapCount++;
            SwLap.Text = $"Lap {_lapCount}   {Fmt(lap)}";
        }
        else
        {
            _sw.Reset(); _lapMark = TimeSpan.Zero; _lapCount = 0; SwLap.Text = "";
        }
        UpdateSwUi();
    }

    // ------------------------------------------------------------------ timer
    void UpdateTimerUi()
    {
        var left = _timerRun ? _timerEnd - DateTime.Now : _timerLeft;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        left = TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds));

        TmText.Text = left.TotalHours >= 1
            ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}"
            : $"{left.Minutes:00}:{left.Seconds:00}";
        TmFill.Width = 200.0 * Math.Clamp(left.TotalSeconds / Math.Max(1.0, _timerSet.TotalSeconds), 0.0, 1.0);

        TmStart.Content = _timerRun ? "Pause" : (_timerLeft < _timerSet ? "Resume" : "Start");
        TmStart.Background = _timerRun ? Orange : Green;
        TmCancel.IsEnabled = _timerRun || _timerLeft < _timerSet;
        TmChips.IsEnabled = !_timerRun && _timerLeft == _timerSet;
    }

    void TickTimer()
    {
        if (_timerEnd - DateTime.Now <= TimeSpan.Zero)
        {
            _timerRun = false;
            _timerLeft = _timerSet;
            UpdateTimerUi();
            Raise(new AlertInfo("Timer", "Time is up", null));
        }
        else if (TabTimer.IsVisible) UpdateTimerUi();
    }

    void OnTmStart(object s, RoutedEventArgs e)
    {
        if (_timerRun) { _timerLeft = _timerEnd - DateTime.Now; _timerRun = false; }
        else
        {
            if (_timerLeft <= TimeSpan.Zero) _timerLeft = _timerSet;
            _timerEnd = DateTime.Now + _timerLeft;
            _timerRun = true;
        }
        UpdateTimerUi();
    }

    void OnTmCancel(object s, RoutedEventArgs e)
    {
        _timerRun = false; _timerLeft = _timerSet; UpdateTimerUi();
    }

    void OnTmPreset(object s, RoutedEventArgs e)
    {
        _timerSet = TimeSpan.FromMinutes(int.Parse((string)((Button)s).Tag));
        _timerLeft = _timerSet;
        UpdateTimerUi();
    }

    // ------------------------------------------------------------------ alarm
    void UpdateAlarmEditor()
    {
        AlHour.Text = _ah.ToString();
        AlMin.Text = _am.ToString("00");
        AlAmPm.Content = _pm ? "PM" : "AM";
    }

    void OnHourUp(object s, RoutedEventArgs e)   { _ah = _ah % 12 + 1; UpdateAlarmEditor(); }
    void OnHourDown(object s, RoutedEventArgs e) { _ah = (_ah + 10) % 12 + 1; UpdateAlarmEditor(); }
    void OnMinUp(object s, RoutedEventArgs e)    { _am = (_am + 1) % 60; UpdateAlarmEditor(); }
    void OnMinDown(object s, RoutedEventArgs e)  { _am = (_am + 59) % 60; UpdateAlarmEditor(); }
    void OnAmPm(object s, RoutedEventArgs e)     { _pm = !_pm; UpdateAlarmEditor(); }

    void OnAddAlarm(object s, RoutedEventArgs e)
    {
        int h24 = _ah % 12 + (_pm ? 12 : 0);
        AddAlarm(h24, _am);
    }

    void AddAlarmAt(DateTime t) => AddAlarm(t.Hour, t.Minute);

    void AddAlarm(int hour, int minute)
    {
        var now = DateTime.Now;
        bool thisMinute = hour * 60 + minute == now.Hour * 60 + now.Minute;
        _data.Alarms.Add(new AlarmItem { Hour = hour, Minute = minute, LastFired = thisMinute ? now.Date : default });
        Save(); RefreshAlarmUi();
    }

    void OnRemoveAlarm(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is AlarmItem a) { _data.Alarms.Remove(a); Save(); RefreshAlarmUi(); }
    }

    void RefreshAlarmUi()
    {
        var sorted = _data.Alarms.OrderBy(a => a.Hour * 60 + a.Minute).ToList();
        AlarmList.ItemsSource = sorted.Take(3).ToList();
        AlarmEmpty.Visibility = sorted.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AlarmMore.Text = sorted.Count > 3 ? $"+{sorted.Count - 3} more" : "";
    }

    // ------------------------------------------------------------------ reminder
    void OnRemChip(object s, RoutedEventArgs e) => _remMin = int.Parse((string)((RadioButton)s).Tag);

    void OnRemKey(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnAddReminder(s, e); e.Handled = true; }
    }

    void OnAddReminder(object s, RoutedEventArgs e)
    {
        string text = RemText.Text.Trim();
        if (text.Length == 0) text = "Reminder";
        _data.Reminders.Add(new ReminderItem { Text = text, Due = DateTime.Now.AddMinutes(_remMin) });
        RemText.Clear();
        Save(); RefreshReminderUi();
    }

    void OnRemoveReminder(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is ReminderItem r) { _data.Reminders.Remove(r); Save(); RefreshReminderUi(); }
    }

    void RefreshReminderUi()
    {
        var sorted = _data.Reminders.OrderBy(r => r.Due).ToList();
        RemList.ItemsSource = sorted.Take(3).ToList();
        RemEmpty.Visibility = sorted.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RemMore.Text = sorted.Count > 3 ? $"+{sorted.Count - 3} more" : "";
    }
}
