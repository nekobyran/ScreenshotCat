using System.Diagnostics;

namespace ScreenshotCat.Services;

internal static class ProcessIdentityService
{
    private static readonly string CurrentProcessName = Process.GetCurrentProcess().ProcessName;

    internal static bool IsCurrentApplicationProcess(uint processId)
    {
        if (processId == 0)
        {
            return false;
        }

        if (processId == Environment.ProcessId)
        {
            return true;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(
                process.ProcessName,
                CurrentProcessName,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsCurrentApplicationProcessName(string processName) =>
        string.Equals(processName, CurrentProcessName, StringComparison.OrdinalIgnoreCase);
}
