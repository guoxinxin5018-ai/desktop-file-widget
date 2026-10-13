using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.VisualBasic.FileIO;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataObject = System.Windows.DataObject;

namespace DesktopFileWidget;

internal static class ShellItemActions
{
    public static void Copy(string path, bool cut)
        => Copy([path], cut);

    public static void Copy(IEnumerable<string> paths, bool cut)
    {
        var data = new WpfDataObject(); var files = new System.Collections.Specialized.StringCollection();
        foreach (string path in paths) files.Add(path);
        data.SetFileDropList(files);
        using var effect = new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5));
        data.SetData("Preferred DropEffect", effect); WpfClipboard.SetDataObject(data, true);
    }

    public static void DeleteToRecycleBin(string path)
    {
        if (Directory.Exists(path)) FileSystem.DeleteDirectory(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
        else FileSystem.DeleteFile(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
    }

    public static bool Rename(Window owner, string path)
    {
        string oldName = Path.GetFileName(path);
        string? name = Prompt(owner, "重命名", "请输入新名称：", oldName);
        if (string.IsNullOrWhiteSpace(name) || name == oldName) return false;
        string destination = Path.Combine(Path.GetDirectoryName(path)!, name);
        if (Directory.Exists(path)) Directory.Move(path, destination); else File.Move(path, destination);
        return true;
    }

    public static void OpenWith(string path) => Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL \"{path}\"") { UseShellExecute = true });
    public static void Properties(string path)
    {
        var info = new SHELLEXECUTEINFO { cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(), fMask = 0x0000000C, hwnd = IntPtr.Zero, lpVerb = "properties", lpFile = path, nShow = 5 };
        ShellExecuteEx(ref info);
    }

    private static string? Prompt(Window owner, string title, string message, string value)
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 390, Height = 165, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var box = new System.Windows.Controls.TextBox { Text = value, Margin = new Thickness(16, 8, 16, 8) };
        var ok = new System.Windows.Controls.Button { Content = "确定", Width = 82, Height = 30, IsDefault = true, Margin = new Thickness(5) };
        var cancel = new System.Windows.Controls.Button { Content = "取消", Width = 82, Height = 30, IsCancel = true, Margin = new Thickness(5) };
        ok.Click += (_, _) => dialog.DialogResult = true;
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var stack = new System.Windows.Controls.StackPanel(); stack.Children.Add(new System.Windows.Controls.TextBlock { Text = message, Margin = new Thickness(16, 12, 16, 0) }); stack.Children.Add(box); stack.Children.Add(buttons); dialog.Content = stack;
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SHELLEXECUTEINFO { public int cbSize; public uint fMask; public IntPtr hwnd; public string? lpVerb; public string? lpFile; public string? lpParameters; public string? lpDirectory; public int nShow; public IntPtr hInstApp; public IntPtr lpIDList; public string? lpClass; public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon; public IntPtr hProcess; }
}
