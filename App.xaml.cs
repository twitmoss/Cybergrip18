using System.Configuration;
using System.Data;
using System.Windows;
using System.Threading;
using System.Windows.Threading;

namespace CyberGrip
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string InstanceMutexName = "Local\\CyberGrip18_SingleInstance_Mutex";
        private const string ActivateEventName = "Local\\CyberGrip18_Activate_Event";

        private Mutex? _instanceMutex;
        private EventWaitHandle? _activateEvent;
        private RegisteredWaitHandle? _activateWait;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Create/open the activation event before checking the mutex so a
            // second launch can always notify the process that owns the app.
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);

            if (!isFirstInstance)
            {
                _activateEvent.Set();
                Shutdown();
                return;
            }

            _activateWait = ThreadPool.RegisterWaitForSingleObject(
                _activateEvent,
                (_, timedOut) =>
                {
                    if (!timedOut)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                        {
                            if (MainWindow is MainWindow window)
                                window.RestoreFromTray();
                        }));
                    }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            bool startInTray = e.Args.Any(argument =>
                string.Equals(argument, "--start-in-tray", StringComparison.OrdinalIgnoreCase));
            if (startInTray)
            {
                mainWindow.ShowInTaskbar = false;
                mainWindow.WindowState = WindowState.Minimized;
            }
            mainWindow.Show();
            if (startInTray)
                mainWindow.Hide();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _activateWait?.Unregister(null);
            _activateEvent?.Dispose();
            if (_instanceMutex != null)
            {
                try { _instanceMutex.ReleaseMutex(); }
                catch (ApplicationException) { }
                _instanceMutex.Dispose();
            }

            base.OnExit(e);
        }
    }

}
