using System.Text.Json;
using System.IO;

namespace DesktopFileWidget;

internal static class HiddenFolderManager
{
    private static readonly string StateFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexTools", "DesktopFileWidget");
    private static readonly string StateFile = Path.Combine(StateFolder, "hidden_by_widget.json");

    public static void Apply(bool hide, IEnumerable<string> desktopFolders)
    {
        var managed = Load();
        if (hide)
        {
            foreach (string folder in desktopFolders.Where(Directory.Exists))
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(folder);
                    if ((attributes & FileAttributes.Hidden) != 0) continue;
                    File.SetAttributes(folder, attributes | FileAttributes.Hidden);
                    managed.Add(folder);
                }
                catch { }
            }
        }
        else
        {
            foreach (string folder in managed.ToArray())
            {
                try
                {
                    if (Directory.Exists(folder))
                    {
                        FileAttributes attributes = File.GetAttributes(folder);
                        File.SetAttributes(folder, attributes & ~FileAttributes.Hidden);
                    }
                    managed.Remove(folder);
                }
                catch { }
            }
        }
        Save(managed);
    }

    private static HashSet<string> Load()
    {
        try
        {
            if (File.Exists(StateFile))
                return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(StateFile))
                    ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private static void Save(HashSet<string> paths)
    {
        try
        {
            Directory.CreateDirectory(StateFolder);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(paths));
        }
        catch { }
    }
}
