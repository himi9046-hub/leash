using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Leash;

public partial class App : Application
{
    private Mutex? _single;
    private MainViewModel? _model;
    private MainWindow? _window;
    private Forms.NotifyIcon? _tray;
    private DispatcherTimer? _timer;
    private string? _lastNewApp;
    private bool _toldAboutTray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _single = new Mutex(true, @"Local\Leash", out var first);
        if (!first && !e.Args.Contains("--elevated"))
        {
            Shutdown();
            return;
        }

        _model = new MainViewModel();
        _model.NewApp += OnNewApp;
        _window = new MainWindow(_model);
        _window.HiddenToTray += OnHiddenToTray;

        _tray = new Forms.NotifyIcon
        {
            Icon = Icons.App(),
            Text = "Leash",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Items.Add("Open", null, (_, _) => ShowWindow());
        _tray.ContextMenuStrip.Items.Add("Quit", null, (_, _) => Quit());
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) ShowWindow();
        };
        _tray.BalloonTipClicked += (_, _) =>
        {
            ShowWindow();
            if (_lastNewApp is not null) _model.Select(_lastNewApp);
        };

        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher);
        Tick();
        _timer.Start();

        if (!e.Args.Contains("--tray")) _window.Show();
    }

    public void Quit()
    {
        _timer?.Stop();
        if (_tray is not null) _tray.Visible = false;
        _tray?.Dispose();
        _model?.Dispose();
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        _single?.Dispose();
        Shutdown();
    }

    private void Tick()
    {
        _model!.Tick();
        if (_tray is not null) _tray.Text = $"Leash  down {_model.Down}  up {_model.Up}";
    }

    private void ShowWindow()
    {
        _window!.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void OnNewApp(string name, string key)
    {
        _lastNewApp = key;
        _tray?.ShowBalloonTip(5000, "New app online", $"{name} just connected to the internet.", Forms.ToolTipIcon.Info);
    }

    private void OnHiddenToTray()
    {
        if (_toldAboutTray) return;
        _toldAboutTray = true;
        _tray?.ShowBalloonTip(3000, "Leash is still running", "It keeps watching from the tray. Right-click the icon to quit.", Forms.ToolTipIcon.None);
    }
}
