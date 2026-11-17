using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace IslandWin;

public partial class App : Application
{
    Mutex _mutex;
    System.Windows.Forms.NotifyIcon _tray;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Log.Error("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        DispatcherUnhandledException += (s, e) =>
        {
            Log.Error("DispatcherUnhandledException", e.Exception);
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Error("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    void OnStartup(object sender, StartupEventArgs e)
    {
        Log.Info("IslandWin starting");

        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i].Equals("--snapshot", StringComparison.OrdinalIgnoreCase))
            {
                string outDir = (i + 1 < e.Args.Length) ? e.Args[i + 1] : "snapshots";
                try
                {
                    Snapshot.Run(outDir);
                    Environment.ExitCode = 0;
                    Shutdown(0);
                }
                catch (Exception ex)
                {
                    Log.Error("Snapshot execution failed", ex);
                    Environment.ExitCode = 1;
                    Shutdown(1);
                }
                return;
            }
        }

        _mutex = new Mutex(true, "IslandWin.SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }

        new MainWindow().Show();
        BuildTray();
    }

    void OnExit(object sender, ExitEventArgs e)
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _mutex?.Dispose();
    }

    void BuildTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var startup = new System.Windows.Forms.ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = Autostart.IsOn
        };
        startup.CheckedChanged += (_, _) => Autostart.Set(startup.Checked);
        menu.Items.Add(startup);
        menu.Items.Add("Quit", null, (_, _) => Shutdown());

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "Island",
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    static System.Drawing.Icon MakeIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var b = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 142, 142, 147));
            g.FillEllipse(b, 3, 10, 26, 12);
        }
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}

static class Autostart
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "IslandWin";

    public static bool IsOn
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
            return k?.GetValue(Name) != null;
        }
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath, true);
        if (k == null) return;
        if (on) k.SetValue(Name, "\"" + Environment.ProcessPath + "\"");
        else k.DeleteValue(Name, false);
    }
}
