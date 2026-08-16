using System.Threading;
using Win11Monitor.App.Services;
using Win11Monitor.App.ViewModels;
using Win11Monitor.App.Views;
using Win11Monitor.Core;

namespace Win11Monitor.App;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\Z690Monitor-0F8A9CC9-159F-4E5D-A6C5-086390A8FB43";
    private const string ActivationEventName = "Local\\Z690Monitor-Activate-7271AE6F-6323-4FA6-B169-9A3F16B19398";

    private Mutex? _singleInstance;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private HardwareMonitorService? _monitor;
    private TrayIconService? _tray;
    private MainWindow? _mainWindow;
    private TaskbarWidgetWindow? _widget;
    private MonitorViewModel? _viewModel;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _singleInstance = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            _ = activationEvent.Set();
            activationEvent.Dispose();
            Shutdown();
            return;
        }

        _activationEvent = activationEvent;

        var settingsService = new SettingsService();
        var startupService = new StartupTaskService();
        var settings = settingsService.Load();

        var monitor = new HardwareMonitorService();
        var viewModel = new MonitorViewModel(monitor, settingsService, startupService, settings);
        var mainWindow = new MainWindow(viewModel);
        var widget = new TaskbarWidgetWindow(viewModel, ShowMainWindow);
        var tray = new TrayIconService(
            showWindow: ShowMainWindow,
            toggleWidget: () => viewModel.ShowTaskbarWidget = !viewModel.ShowTaskbarWidget,
            exit: ExitApplication);
        tray.SetWidgetChecked(viewModel.ShowTaskbarWidget);

        _monitor = monitor;
        _viewModel = viewModel;
        _mainWindow = mainWindow;
        _widget = widget;
        _tray = tray;
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            OnActivationRequested,
            null,
            Timeout.Infinite,
            false);

        viewModel.SnapshotUpdated += OnSnapshotUpdated;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        mainWindow.Closing += (_, args) =>
        {
            if (!viewModel.IsExiting)
            {
                args.Cancel = true;
                mainWindow.Hide();
            }
        };

        widget.RefreshVisibility();
        if (!e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase))
        {
            mainWindow.Show();
            mainWindow.Activate();
        }

        viewModel.Start();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = System.Windows.WindowState.Normal;
        _mainWindow.Activate();
    }

    private void OnActivationRequested(object? state, bool timedOut)
    {
        if (!timedOut && !Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.BeginInvoke(ShowMainWindow);
        }
    }

    private void ExitApplication()
    {
        _ = _activationRegistration?.Unregister(null);
        _activationEvent?.Dispose();

        if (_viewModel is not null)
        {
            _viewModel.IsExiting = true;
        }

        Shutdown();
    }

    private void OnSnapshotUpdated(object? sender, MonitorSnapshot snapshot)
    {
        _tray?.Update(snapshot);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MonitorViewModel.ShowTaskbarWidget) || _widget is null || _viewModel is null)
        {
            return;
        }

        _widget.RefreshVisibility();

        _tray?.SetWidgetChecked(_viewModel.ShowTaskbarWidget);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.IsExiting = true;
            _viewModel.Dispose();
        }

        _tray?.Dispose();
        _monitor?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
