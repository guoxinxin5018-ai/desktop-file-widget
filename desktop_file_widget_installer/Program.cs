using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace DesktopFileWidgetInstaller;

internal static class Program
{
    private const string ProductName = "桌面文件小组件";
    private const string ExeName = "桌面文件小组件.exe";
    private static string InstallFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexTools", "DesktopFileWidget");
    private static string InstalledExe => Path.Combine(InstallFolder, ExeName);

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var choice = MessageBox.Show(
            "桌面文件小组件\n\n【是】安装或更新\n【否】卸载\n【取消】退出\n\n安装后将自动随 Windows 登录启动。",
            "安装与卸载", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
        if (choice == DialogResult.Cancel) return;
        try
        {
            if (choice == DialogResult.Yes) Install();
            else Uninstall();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"操作失败：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Install()
    {
        StopRunning();
        Directory.CreateDirectory(InstallFolder);
        using Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.DesktopFileWidget.exe")
            ?? throw new InvalidOperationException("安装资源缺失。");
        using (FileStream target = File.Create(InstalledExe)) source.CopyTo(target);

        using RegistryKey run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        run.SetValue("CodexDesktopFileWidget", $"\"{InstalledExe}\"");
        run.DeleteValue("CodexDesktopReplacement", false);

        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true });
        RestoreSystemDesktopIcons();
        MessageBox.Show("安装完成。\n\n桌面文件小组件已经启动；桌面替换功能已移除，并已恢复 Windows 原桌面图标。",
            "安装完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void Uninstall()
    {
        if (MessageBox.Show("确定卸载桌面文件小组件吗？\n桌面上的文件不会被删除。", "确认卸载",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        StopRunning();
        RestoreSystemDesktopIcons();
        RestoreHiddenFolders();
        using (RegistryKey? run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
        {
            run?.DeleteValue("CodexDesktopFileWidget", false);
            run?.DeleteValue("CodexDesktopReplacement", false);
        }
        if (Directory.Exists(InstallFolder)) Directory.Delete(InstallFolder, true);
        MessageBox.Show("卸载完成，桌面文件未作任何改动。", "卸载完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void RestoreHiddenFolders()
    {
        string stateFile = Path.Combine(InstallFolder, "hidden_by_widget.json");
        if (!File.Exists(stateFile)) return;
        try
        {
            string[] paths = JsonSerializer.Deserialize<string[]>(File.ReadAllText(stateFile)) ?? [];
            foreach (string path in paths)
            {
                try
                {
                    if (!Directory.Exists(path)) continue;
                    FileAttributes attributes = File.GetAttributes(path);
                    File.SetAttributes(path, attributes & ~FileAttributes.Hidden);
                }
                catch { }
            }
        }
        catch { }
    }

    private static void StopRunning()
    {
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            try { process.Kill(true); process.WaitForExit(3000); } catch { }
        }
    }

    private static void RestoreSystemDesktopIcons()
    {
        try
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr def = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (def == IntPtr.Zero)
            {
                IntPtr worker = IntPtr.Zero;
                while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
                {
                    def = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (def != IntPtr.Zero) break;
                }
            }
            IntPtr list = def == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(def, IntPtr.Zero, "SysListView32", null);
            if (list != IntPtr.Zero) ShowWindow(list, 5);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}
