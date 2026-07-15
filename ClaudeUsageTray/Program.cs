// version 7
using System.Threading;

namespace ClaudeUsageTray;

internal static class Program
{
    private const string MutexName = @"Local\SWAKES.ClaudeUsageTray";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var appMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Claude Usage Tray is already running.",
                "Claude Usage Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.Run(new TrayApplicationContext());
    }
}
