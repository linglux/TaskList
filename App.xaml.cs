using System.Windows;
using TaskList.Data;

namespace TaskList;

public partial class App : Application
{
    private const string MutexName = @"Local\TaskList.SingleInstance.Mutex";
    private const string ActivateEventName = @"Local\TaskList.SingleInstance.Activate";

    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool ownsInstance;
        _mutex = new Mutex(true, MutexName, out ownsInstance);
        if (!ownsInstance)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ActivateEventName);
                existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            catch (UnauthorizedAccessException) { }
            Shutdown();
            return;
        }

        base.OnStartup(e);

        try
        {
            Database.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"本地数据库初始化失败：\n{ex.Message}", "TaskList",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _ = Task.Run(ActivationLoop);
    }

    private void ActivationLoop()
    {
        while (true)
        {
            if (_activateEvent is null || !_activateEvent.WaitOne()) return;
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (MainWindow is MainWindow window) window.ShowFromTray();
                });
            }
            catch (OperationCanceledException) { return; }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is MainWindow window) window.Cleanup();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"发生未处理的错误：\n{e.Exception.Message}", "TaskList",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
