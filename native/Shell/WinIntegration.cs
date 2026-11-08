using System.Diagnostics;
using System.Runtime.InteropServices;
using HomeControl.Core;
using Microsoft.Win32;

namespace HomeControl.Shell;

/// <summary>Start with Windows, uninstall, and the few Win32 calls WinUI doesn't cover.</summary>
public static partial class WinIntegration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = AppInfo.Name;
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppInfo.Name;

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppInfo.ExeName);

    static string StartupCommand => $"\"{ExePath}\" --minimized";

    public static bool StartupEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunValue) is string v && v.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetStartup(bool enabled)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) k.SetValue(RunValue, StartupCommand);
        else k.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    /// <summary>"HomeControl.exe --uninstall", run from Settings > Apps.</summary>
    public static int Uninstall()
    {
        if (MessageBoxW(IntPtr.Zero, $"Remove {AppInfo.Name} from this PC?\n\nYour settings are kept in %APPDATA%\\{AppInfo.Name}.",
                        $"Uninstall {AppInfo.Name}", 0x24 /* MB_YESNO | MB_ICONQUESTION */) != 6 /* IDYES */)
            return 1;
        int me = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppInfo.ExeName)))
        {
            if (p.Id == me) continue;
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
        try { SetStartup(false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk");
        try { File.Delete(shortcut); } catch { }

        // This exe can't delete its own folder while running: a hidden cmd does it once we've exited.
        string dir = AppContext.BaseDirectory.TrimEnd('\\');
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{dir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        });
        MessageBoxW(IntPtr.Zero, $"{AppInfo.Name} has been removed.", $"Uninstall {AppInfo.Name}", 0x40 /* MB_ICONINFORMATION */);
        return 0;
    }

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr hWnd);
}
