using HomeControl.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace HomeControl;

public static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (args.Contains("--uninstall")) return WinIntegration.Uninstall();
        HomeControl.Core.AppInfo.MoveOldSettings();

        // One copy per user: a second launch just brings the running window forward.
        // (Screenshot mode renders off-screen and exits, so it may run beside the real app.)
        if (!args.Contains("--screenshots"))
        {
            var instance = AppInstance.FindOrRegisterForKey("HomeControl-" + Environment.UserName);
            if (!instance.IsCurrent)
            {
                WinIntegration.AllowSetForegroundWindow((uint)instance.ProcessId);
                var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                Task.Run(() => instance.RedirectActivationToAsync(activation).AsTask()).Wait(5000);
                return 0;
            }
            instance.Activated += (_, _) => App.Instance?.OnRedirectedActivation();
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(args);
        });
        return 0;
    }
}
