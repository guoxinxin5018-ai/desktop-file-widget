using System.Text.Json;
using System.IO;

namespace DesktopFileWidget;

internal sealed class WidgetSettings
{
    public string Name { get; set; } = "桌面文件";
    public int BackColorArgb { get; set; } = unchecked((int)0xFF26364E);
    public int OpacityPercent { get; set; } = 88;
    public bool Locked { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool ShowFoldersOnly { get; set; }
    public bool HideFoldersOnDesktop { get; set; }
    public bool FollowSystemTheme { get; set; }
    public string SortBy { get; set; } = "Name";
    public bool SortDescending { get; set; }
    public int X { get; set; } = 80;
    public int Y { get; set; } = 80;
    public int Width { get; set; } = 430;
    public int Height { get; set; } = 540;

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexTools", "DesktopFileWidget");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public static WidgetSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
