using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenshotCat.Interop;

namespace ScreenshotCat.Services;

public sealed class PersistentWindowService
{
    private readonly string _statePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenshotCat",
        "persistent-windows.json");
    private readonly List<PersistentWindowTarget> _targets;

    public PersistentWindowService()
    {
        _targets = Load();
        if (_targets.RemoveAll(target =>
                ProcessIdentityService.IsCurrentApplicationProcessName(target.ProcessName)) > 0)
        {
            Save();
        }
    }

    public void Remember(nint hwnd)
    {
        var target = Describe(hwnd);
        if (target is null || _targets.Contains(target))
        {
            return;
        }

        _targets.Add(target);
        Save();
    }

    public void Forget(nint hwnd)
    {
        var target = Describe(hwnd);
        if (target is null)
        {
            return;
        }

        var removed = _targets.RemoveAll(item => item == target);
        if (removed == 0)
        {
            _targets.RemoveAll(item => string.Equals(
                item.ProcessName,
                target.ProcessName,
                StringComparison.OrdinalIgnoreCase));
        }
        Save();
    }

    public IReadOnlyList<nint> ResolveAvailableWindows()
    {
        var candidates = new List<(nint Hwnd, string ProcessName, string Title)>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            var description = Describe(hwnd);
            if (description is not null)
            {
                candidates.Add((hwnd, description.ProcessName, description.WindowTitle));
            }

            return true;
        }, 0);

        var resolved = new List<nint>();
        foreach (var target in _targets)
        {
            var processMatches = candidates
                .Where(candidate => string.Equals(
                    candidate.ProcessName,
                    target.ProcessName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var exact = processMatches.FirstOrDefault(candidate =>
                string.Equals(candidate.Title, target.WindowTitle, StringComparison.Ordinal));
            if (exact.Hwnd != 0)
            {
                resolved.Add(exact.Hwnd);
            }
            else if (processMatches.Length == 1)
            {
                resolved.Add(processMatches[0].Hwnd);
            }
        }

        return resolved.Distinct().ToArray();
    }

    private static PersistentWindowTarget? Describe(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0 || ProcessIdentityService.IsCurrentApplicationProcess(processId))
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var titleBuffer = new StringBuilder(512);
            _ = NativeMethods.GetWindowText(hwnd, titleBuffer, titleBuffer.Capacity);
            var title = titleBuffer.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            return new PersistentWindowTarget(process.ProcessName, title);
        }
        catch
        {
            return null;
        }
    }

    private List<PersistentWindowTarget> Load()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize(
                    File.ReadAllText(_statePath),
                    PersistentWindowJsonContext.Default.ListPersistentWindowTarget)
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(
            _statePath,
            JsonSerializer.Serialize(_targets, PersistentWindowJsonContext.Default.ListPersistentWindowTarget));
    }
}

public sealed record PersistentWindowTarget(string ProcessName, string WindowTitle);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<PersistentWindowTarget>))]
internal partial class PersistentWindowJsonContext : JsonSerializerContext;
