using System.ComponentModel;
using System.Diagnostics;

namespace NovelShelf.App.Services;

public static class DesktopNotifier
{
    public static void Show(string title, string message)
    {
        if (!OperatingSystem.IsLinux())
            return;
        try
        {
            var start = new ProcessStartInfo("notify-send") { UseShellExecute = false };
            foreach (var arg in new[] { "--app-name=NovelShelf", "--icon=accessories-dictionary", title, message })
                start.ArgumentList.Add(arg);
            Process.Start(start)?.Dispose();
        }
        catch (Win32Exception)
        {
        }
    }
}
