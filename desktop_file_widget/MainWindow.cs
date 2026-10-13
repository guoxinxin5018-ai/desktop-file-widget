using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Automation;
using MediaColor = System.Windows.Media.Color;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using Button = System.Windows.Controls.Button;

namespace DesktopFileWidget;

internal sealed class MainWindow : Window
{
    private readonly WidgetSettings settings = WidgetSettings.Load();
    private readonly Border root = new();
    private readonly Border header = new();
    private readonly TextBlock title = new();
    private readonly TextBlock countText = new();
    private readonly WrapPanel foldersPanel = new();
    private readonly WrapPanel filesPanel = new();
    private readonly StackPanel filesSection = new();
    private readonly StackPanel installersSection = new();
    private readonly TextBlock foldersHeading = new();
    private readonly TextBlock filesHeading = new();
    private readonly TextBlock installersHeading = new();
    private readonly WrapPanel installersPanel = new();
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private readonly string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    private MenuItem lockItem = null!;
    private MenuItem foldersOnlyItem = null!;
    private MenuItem hideDesktopFoldersItem = null!;
    private DateTime latestSnapshot;
    private readonly List<Border> tileOrder = [];
    private readonly HashSet<Border> selectedTiles = [];
    private int selectionAnchor = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string displayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string typeName;
    }
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0;

    public MainWindow()
    {
        Title = settings.Name;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = settings.AlwaysOnTop;
        Left = settings.X; Top = settings.Y; Width = Math.Max(280, settings.Width); Height = Math.Max(280, settings.Height);
        MinWidth = 280; MinHeight = 280;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        AllowDrop = true;

        BuildUi();
        BuildMenu();
        ApplyLockState();
        ApplyDesktopFolderVisibility();
        RefreshItems();
        ApplyTheme();

        MouseMove += (_, _) => { };
        DragEnter += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy; };
        Drop += OnDrop;
        LocationChanged += (_, _) => SaveBounds();
        SizeChanged += (_, _) => SaveBounds();
        Closing += (_, _) => { SystemEvents.UserPreferenceChanged -= OnPreferenceChanged; SaveBounds(); };
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        refreshTimer.Tick += (_, _) => RefreshIfChanged();
        refreshTimer.Start();
    }

    private void BuildUi()
    {
        root.CornerRadius = new CornerRadius(16);
        root.ClipToBounds = true;
        root.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 1) Keyboard.Focus(root); };
        root.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0 && !tileOrder.Any(t => t.IsMouseOver))
            {
                ClearTileSelection(); selectionAnchor = -1;
                SyncDesktopSelection();
            }
        };
        Content = root;

        var layout = new DockPanel(); root.Child = layout;
        header.Height = 58; header.Background = new SolidColorBrush(MediaColor.FromArgb(26, 255, 255, 255));
        header.Padding = new Thickness(18, 9, 12, 6); DockPanel.SetDock(header, Dock.Top);
        header.MouseLeftButtonDown += (_, e) => { if (!settings.Locked && e.ChangedButton == MouseButton.Left) DragMove(); };
        var headerGrid = new Grid(); header.Child = headerGrid;
        var headerStack = new StackPanel(); headerGrid.Children.Add(headerStack);
        title.Text = settings.Name; title.FontSize = 17; title.FontWeight = FontWeights.SemiBold;
        countText.FontSize = 12; countText.Margin = new Thickness(0, 2, 0, 0);
        headerStack.Children.Add(title); headerStack.Children.Add(countText);
        var more = new Button { Content = "⋯", Width = 36, Height = 32, FontSize = 20, Padding = new Thickness(0, 0, 0, 5), HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = System.Windows.VerticalAlignment.Center, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, ToolTip = "设置和更多" };
        more.MouseEnter += (_, _) => more.Background = new SolidColorBrush(MediaColor.FromArgb(38, 255, 255, 255));
        more.MouseLeave += (_, _) => more.Background = Brushes.Transparent;
        more.Click += (_, _) => { if (root.ContextMenu is { } menu) { menu.PlacementTarget = more; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; } };
        headerGrid.Children.Add(more);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var content = new StackPanel { Margin = new Thickness(12, 8, 12, 16) };
        foldersHeading.Text = "文件夹"; foldersHeading.FontSize = 12; foldersHeading.FontWeight = FontWeights.SemiBold; foldersHeading.Margin = new Thickness(6, 3, 0, 4);
        filesHeading.Text = "其他文件"; filesHeading.FontSize = 12; filesHeading.FontWeight = FontWeights.SemiBold; filesHeading.Margin = new Thickness(6, 10, 0, 4);
        installersHeading.Text = "安装程序"; installersHeading.FontSize = 12; installersHeading.FontWeight = FontWeights.SemiBold; installersHeading.Margin = new Thickness(6, 10, 0, 4);
        var divider = new Border { Height = 1, Margin = new Thickness(6, 8, 6, 0), Background = new SolidColorBrush(MediaColor.FromArgb(70, 255, 255, 255)) };
        var installerDivider = new Border { Height = 1, Margin = new Thickness(6, 8, 6, 0), Background = new SolidColorBrush(MediaColor.FromArgb(70, 255, 255, 255)) };
        content.Children.Add(foldersHeading); content.Children.Add(foldersPanel);
        filesSection.Children.Add(divider); filesSection.Children.Add(filesHeading); filesSection.Children.Add(filesPanel);
        installersSection.Children.Add(installerDivider); installersSection.Children.Add(installersHeading); installersSection.Children.Add(installersPanel);
        content.Children.Add(filesSection); content.Children.Add(installersSection); scroll.Content = content;
        layout.Children.Add(header); layout.Children.Add(scroll);
    }

    private void BuildMenu()
    {
        var menu = new ContextMenu();
        var refresh = new MenuItem { Header = "刷新" }; refresh.Click += (_, _) => RefreshItems(); menu.Items.Add(refresh);
        var openDesktop = new MenuItem { Header = "打开桌面文件夹" }; openDesktop.Click += (_, _) => OpenPath(userDesktop); menu.Items.Add(openDesktop);

        foldersOnlyItem = new MenuItem { Header = "只显示文件夹", IsCheckable = true, IsChecked = settings.ShowFoldersOnly };
        foldersOnlyItem.Click += (_, _) => { settings.ShowFoldersOnly = foldersOnlyItem.IsChecked; settings.Save(); RefreshItems(); };
        menu.Items.Add(foldersOnlyItem);

        hideDesktopFoldersItem = new MenuItem { Header = "从原桌面隐藏文件夹", IsCheckable = true, IsChecked = settings.HideFoldersOnDesktop };
        hideDesktopFoldersItem.Click += (_, _) => { settings.HideFoldersOnDesktop = hideDesktopFoldersItem.IsChecked; settings.Save(); ApplyDesktopFolderVisibility(); };
        menu.Items.Add(hideDesktopFoldersItem); menu.Items.Add(new Separator());

        var sort = new MenuItem { Header = "排序方式" };
        AddSortChoice(sort, "名称", "Name");
        AddSortChoice(sort, "类型", "Type");
        AddSortChoice(sort, "修改时间", "Modified");
        AddSortChoice(sort, "大小", "Size");
        sort.Items.Add(new Separator());
        var ascending = new MenuItem { Header = "升序", IsCheckable = true, IsChecked = !settings.SortDescending };
        var descending = new MenuItem { Header = "降序", IsCheckable = true, IsChecked = settings.SortDescending };
        ascending.Click += (_, _) => { settings.SortDescending = false; settings.Save(); RefreshItems(); };
        descending.Click += (_, _) => { settings.SortDescending = true; settings.Save(); RefreshItems(); };
        sort.SubmenuOpened += (_, _) => { ascending.IsChecked = !settings.SortDescending; descending.IsChecked = settings.SortDescending; };
        sort.Items.Add(ascending); sort.Items.Add(descending); menu.Items.Add(sort);
        menu.Items.Add(new Separator());

        lockItem = new MenuItem { Header = "锁定位置", IsCheckable = true, IsChecked = settings.Locked };
        lockItem.Click += (_, _) => { settings.Locked = lockItem.IsChecked; settings.Save(); ApplyLockState(); };
        menu.Items.Add(lockItem);
        var setup = new MenuItem { Header = "设置…" }; setup.Click += (_, _) => ShowSettings(); menu.Items.Add(setup);
        menu.Items.Add(new Separator());
        var exit = new MenuItem { Header = "退出小组件" }; exit.Click += (_, _) => Close(); menu.Items.Add(exit);
        root.ContextMenu = menu;
    }

    private void AddSortChoice(MenuItem parent, string label, string value)
    {
        var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = settings.SortBy == value };
        item.Click += (_, _) => { settings.SortBy = value; settings.Save(); RefreshItems(); };
        parent.SubmenuOpened += (_, _) => item.IsChecked = settings.SortBy == value;
        parent.Items.Add(item);
    }

    private void RefreshIfChanged()
    {
        try
        {
            DateTime stamp = VisibleItems().Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
            if (stamp != latestSnapshot) RefreshItems();
        }
        catch { }
    }

    private void RefreshItems()
    {
        string[] paths;
        try { paths = VisibleItems().Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch { return; }
        string[] folders = SortPaths(paths.Where(Directory.Exists)).ToArray();
        string[] files = settings.ShowFoldersOnly ? [] : SortPaths(paths.Where(p => IsRegularFile(p) && !IsInstallerFile(p))).ToArray();
        string[] installers = settings.ShowFoldersOnly ? [] : SortPaths(paths.Where(IsInstallerFile)).ToArray();
        foldersPanel.Children.Clear(); filesPanel.Children.Clear(); installersPanel.Children.Clear(); tileOrder.Clear(); selectedTiles.Clear(); selectionAnchor = -1;
        foreach (string path in folders) foldersPanel.Children.Add(CreateTile(path));
        foreach (string path in files) filesPanel.Children.Add(CreateTile(path));
        foreach (string path in installers) installersPanel.Children.Add(CreateTile(path));
        filesSection.Visibility = settings.ShowFoldersOnly ? Visibility.Collapsed : Visibility.Visible;
        installersSection.Visibility = settings.ShowFoldersOnly || installers.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        countText.Text = settings.ShowFoldersOnly ? $"{folders.Length} 个文件夹  ·  右键设置" : $"{folders.Length} 个文件夹，{files.Length} 个文件，{installers.Length} 个安装程序  ·  右键设置";
        latestSnapshot = paths.Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
        ApplyTextColors();
    }

    private IEnumerable<string> SortPaths(IEnumerable<string> source)
    {
        Func<string, object> key = settings.SortBy switch
        {
            "Type" => p => Directory.Exists(p) ? "" : Path.GetExtension(p),
            "Modified" => p => File.GetLastWriteTimeUtc(p),
            "Size" => p => Directory.Exists(p) ? 0L : SafeLength(p),
            _ => p => Path.GetFileName(p)
        };
        return settings.SortDescending
            ? source.OrderByDescending(key).ThenBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
            : source.OrderBy(key).ThenBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase);
    }

    private static long SafeLength(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }
    private static bool IsRegularFile(string path)
    {
        if (Directory.Exists(path)) return false;
        string ext = Path.GetExtension(path);
        return !ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".url", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInstallerFile(string path)
    {
        if (Directory.Exists(path)) return false;
        return Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".msi" or ".msix" or ".appx" or ".appxbundle" or ".msixbundle" or ".msp";
    }

    private FrameworkElement CreateTile(string path)
    {
        var tile = new Border { Width = 102, Height = 96, Margin = new Thickness(4), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = path };
        tileOrder.Add(tile);
        var stack = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        var image = new Image { Width = 44, Height = 44, Margin = new Thickness(0, 5, 0, 4), Source = GetShellIcon(path) };
        var label = new TextBlock { Text = Path.GetFileName(path), Width = 96, MaxHeight = 39, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
        stack.Children.Add(image); stack.Children.Add(label); tile.Child = stack;
        tile.MouseEnter += (_, _) => { if (!selectedTiles.Contains(tile)) tile.Background = new SolidColorBrush(MediaColor.FromArgb(35, 255, 255, 255)); };
        tile.MouseLeave += (_, _) => tile.Background = selectedTiles.Contains(tile) ? new SolidColorBrush(MediaColor.FromArgb(80, 90, 175, 235)) : Brushes.Transparent;
        tile.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { OpenPath(path); e.Handled = true; return; }
            SelectTile(tile); e.Handled = true;
        };
        tile.PreviewMouseRightButtonDown += (_, _) => { if (!selectedTiles.Contains(tile)) { ClearTileSelection(); SetTileSelected(tile, true); selectionAnchor = tileOrder.IndexOf(tile); SyncDesktopSelection(); } };
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "打开" }; open.Click += (_, _) => { foreach (string item in ActionPaths(tile, path)) OpenPath(item); }; menu.Items.Add(open);
        MenuItem? openWith = null;
        if (!Directory.Exists(path)) { openWith = new MenuItem { Header = "打开方式…" }; openWith.Click += (_, _) => ShellItemActions.OpenWith(path); menu.Items.Add(openWith); }
        menu.Items.Add(new Separator());
        var cut = new MenuItem { Header = "剪切" }; cut.Click += (_, _) => ShellItemActions.Copy(ActionPaths(tile, path), true); menu.Items.Add(cut);
        var copy = new MenuItem { Header = "复制" }; copy.Click += (_, _) => ShellItemActions.Copy(ActionPaths(tile, path), false); menu.Items.Add(copy);
        var delete = new MenuItem { Header = "删除" }; delete.Click += (_, _) => { try { foreach (string item in ActionPaths(tile, path).ToArray()) ShellItemActions.DeleteToRecycleBin(item); RefreshItems(); } catch (Exception ex) { WpfMessageBox.Show(ex.Message); } }; menu.Items.Add(delete);
        var rename = new MenuItem { Header = "重命名" }; rename.Click += (_, _) => { try { string item = ActionPaths(tile, path).Single(); if (ShellItemActions.Rename(this, item)) RefreshItems(); } catch (Exception ex) { WpfMessageBox.Show(ex.Message); } }; menu.Items.Add(rename);
        menu.Items.Add(new Separator());
        var locate = new MenuItem { Header = "打开所在位置" }; locate.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); menu.Items.Add(locate);
        var properties = new MenuItem { Header = "属性" }; properties.Click += (_, _) => { foreach (string item in ActionPaths(tile, path)) ShellItemActions.Properties(item); }; menu.Items.Add(properties);
        menu.Opened += (_, _) => { int count = ActionPaths(tile, path).Count(); rename.IsEnabled = count == 1; if (openWith != null) openWith.IsEnabled = count == 1; };
        tile.ContextMenu = menu;
        return tile;
    }

    private IEnumerable<string> ActionPaths(Border clicked, string fallback)
    {
        if (!selectedTiles.Contains(clicked)) return [fallback];
        return tileOrder.Where(selectedTiles.Contains).Select(t => t.Tag as string).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToArray();
    }

    private void SelectTile(Border tile)
    {
        int index = tileOrder.IndexOf(tile);
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (shift && selectionAnchor >= 0)
        {
            if (!ctrl) ClearTileSelection();
            for (int i = Math.Min(selectionAnchor, index); i <= Math.Max(selectionAnchor, index); i++) SetTileSelected(tileOrder[i], true);
        }
        else if (ctrl)
        {
            SetTileSelected(tile, !selectedTiles.Contains(tile)); selectionAnchor = index;
        }
        else
        {
            ClearTileSelection(); SetTileSelected(tile, true); selectionAnchor = index;
        }
        SyncDesktopSelection();
    }

    private void SetTileSelected(Border tile, bool selected)
    {
        if (selected) selectedTiles.Add(tile); else selectedTiles.Remove(tile);
        tile.Background = selected ? new SolidColorBrush(MediaColor.FromArgb(80, 90, 175, 235)) : Brushes.Transparent;
    }

    private void ClearTileSelection()
    {
        foreach (Border tile in selectedTiles.ToArray()) SetTileSelected(tile, false);
    }

    private void SyncDesktopSelection()
    {
        try
        {
            IntPtr list = FindDesktopListView();
            if (list == IntPtr.Zero) return;
            AutomationElement desktopList = AutomationElement.FromHandle(list);
            AutomationElementCollection icons = desktopList.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            var wanted = selectedTiles.Select(t => t.Tag as string).Where(p => !string.IsNullOrEmpty(p))
                .SelectMany(p => new[] { Path.GetFileName(p!), Path.GetFileNameWithoutExtension(p!) })
                .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            bool first = true;
            foreach (AutomationElement icon in icons)
            {
                if (!icon.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object patternObject)) continue;
                var pattern = (SelectionItemPattern)patternObject;
                if (wanted.Contains(icon.Current.Name))
                {
                    if (first) { pattern.Select(); first = false; } else pattern.AddToSelection();
                }
                else if (pattern.Current.IsSelected) pattern.RemoveFromSelection();
            }
        }
        catch { }
    }

    private static IntPtr FindDesktopListView()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView == IntPtr.Zero)
        {
            IntPtr worker = IntPtr.Zero;
            while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            {
                defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero) break;
            }
        }
        return defView == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    private static ImageSource? GetShellIcon(string path)
    {
        var info = new SHFILEINFO();
        IntPtr result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(48, 48));
            source.Freeze(); return source;
        }
        finally { DestroyIcon(info.hIcon); }
    }

    private IEnumerable<string> DesktopItems()
    {
        if (Directory.Exists(userDesktop)) foreach (string p in Directory.EnumerateFileSystemEntries(userDesktop)) yield return p;
        if (Directory.Exists(publicDesktop)) foreach (string p in Directory.EnumerateFileSystemEntries(publicDesktop)) yield return p;
    }
    private IEnumerable<string> VisibleItems() => DesktopItems().Where(p => !settings.ShowFoldersOnly || Directory.Exists(p));

    private void ShowSettings()
    {
        var dialog = new SettingsWindow(settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        settings.Name = string.IsNullOrWhiteSpace(dialog.WidgetName) ? "桌面文件" : dialog.WidgetName;
        settings.BackColorArgb = dialog.ColorArgb;
        settings.OpacityPercent = dialog.OpacityPercent;
        settings.AlwaysOnTop = dialog.AlwaysOnTop;
        settings.ShowFoldersOnly = dialog.ShowFoldersOnly;
        settings.HideFoldersOnDesktop = dialog.HideFoldersOnDesktop;
        settings.FollowSystemTheme = dialog.FollowSystemTheme;
        settings.Save();
        Title = title.Text = settings.Name; Topmost = settings.AlwaysOnTop;
        foldersOnlyItem.IsChecked = settings.ShowFoldersOnly;
        hideDesktopFoldersItem.IsChecked = settings.HideFoldersOnDesktop;
        ApplyDesktopFolderVisibility(); ApplyTheme(); RefreshItems();
    }

    private void ApplyTheme()
    {
        MediaColor color = EffectiveColor();
        byte alpha = (byte)Math.Clamp((int)Math.Round(settings.OpacityPercent * 2.55), 10, 255);
        root.Background = new SolidColorBrush(MediaColor.FromArgb(alpha, color.R, color.G, color.B));
        ApplyTextColors();
    }

    private void ApplyTextColors()
    {
        bool light = RelativeLuminance(EffectiveColor()) > .58;
        Brush main = light ? new SolidColorBrush(MediaColor.FromRgb(28, 38, 52)) : Brushes.White;
        Brush secondary = light ? new SolidColorBrush(MediaColor.FromRgb(65, 80, 100)) : new SolidColorBrush(MediaColor.FromRgb(210, 222, 236));
        title.Foreground = main; countText.Foreground = secondary;
        foldersHeading.Foreground = secondary; filesHeading.Foreground = secondary; installersHeading.Foreground = secondary;
        foreach (Border tile in foldersPanel.Children.OfType<Border>().Concat(filesPanel.Children.OfType<Border>()).Concat(installersPanel.Children.OfType<Border>()))
            if (tile.Child is StackPanel stack)
                foreach (TextBlock label in stack.Children.OfType<TextBlock>()) label.Foreground = main;
    }

    private MediaColor EffectiveColor() => settings.FollowSystemTheme
        ? (IsLightTheme() ? MediaColor.FromRgb(238, 243, 248) : MediaColor.FromRgb(38, 54, 78))
        : FromArgb(settings.BackColorArgb);
    private static double RelativeLuminance(MediaColor c) => (.2126 * c.R + .7152 * c.G + .0722 * c.B) / 255.0;
    private static MediaColor FromArgb(int argb) => MediaColor.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    private static bool IsLightTheme()
    {
        try { return Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1)) != 0; }
        catch { return false; }
    }
    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (settings.FollowSystemTheme) Dispatcher.BeginInvoke(ApplyTheme);
    }

    private void ApplyLockState() => ResizeMode = settings.Locked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
    private void ApplyDesktopFolderVisibility() => HiddenFolderManager.Apply(settings.HideFoldersOnDesktop, DesktopItems().Where(Directory.Exists));
    private void SaveBounds()
    {
        if (WindowState != WindowState.Normal) return;
        settings.X = (int)Left; settings.Y = (int)Top; settings.Width = (int)Width; settings.Height = (int)Height; settings.Save();
    }

    private static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { WpfMessageBox.Show($"无法打开：\n{ex.Message}", "桌面文件小组件", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OnDrop(object sender, WpfDragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (string source in paths)
        {
            try
            {
                string destination = UniqueDestination(Path.Combine(userDesktop, Path.GetFileName(source)));
                if (Directory.Exists(source)) CopyDirectory(source, destination); else File.Copy(source, destination);
            }
            catch (Exception ex) { WpfMessageBox.Show($"添加失败：{Path.GetFileName(source)}\n{ex.Message}", "桌面文件小组件", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        ApplyDesktopFolderVisibility(); RefreshItems();
    }

    private static string UniqueDestination(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!; string stem = Path.GetFileNameWithoutExtension(path); string ext = Path.GetExtension(path);
        for (int i = 2; ; i++) { string candidate = Path.Combine(dir, $"{stem} ({i}){ext}"); if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate; }
    }
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string folder in Directory.EnumerateDirectories(source)) CopyDirectory(folder, Path.Combine(destination, Path.GetFileName(folder)));
    }
}
