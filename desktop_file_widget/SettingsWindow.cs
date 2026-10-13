using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;

namespace DesktopFileWidget;

internal sealed class SettingsWindow : Window
{
    private readonly WpfTextBox nameBox = new();
    private readonly Border colorPreview = new();
    private readonly Slider opacitySlider = new();
    private readonly TextBlock opacityText = new();
    private readonly WpfCheckBox topMost = new();
    private readonly WpfCheckBox foldersOnly = new();
    private readonly WpfCheckBox hideDesktopFolders = new();
    private readonly WpfCheckBox followTheme = new();
    private MediaColor selectedColor;

    public string WidgetName => nameBox.Text.Trim();
    public int ColorArgb => selectedColor.A << 24 | selectedColor.R << 16 | selectedColor.G << 8 | selectedColor.B;
    public int OpacityPercent => (int)Math.Round(opacitySlider.Value);
    public bool AlwaysOnTop => topMost.IsChecked == true;
    public bool ShowFoldersOnly => foldersOnly.IsChecked == true;
    public bool HideFoldersOnDesktop => hideDesktopFolders.IsChecked == true;
    public bool FollowSystemTheme => followTheme.IsChecked == true;

    public SettingsWindow(WidgetSettings settings)
    {
        Title = "小组件设置";
        Width = 460; Height = 465;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.White;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        selectedColor = FromArgb(settings.BackColorArgb);

        var grid = new Grid { Margin = new Thickness(26) };
        for (int i = 0; i < 9; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 8 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Content = grid;

        AddLabel(grid, "组件名称", 0);
        nameBox.Text = settings.Name; nameBox.Margin = new Thickness(0, 0, 0, 15); Put(grid, nameBox, 0, 1);

        AddLabel(grid, "背景颜色", 1);
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };
        colorPreview.Width = 110; colorPreview.Height = 32; colorPreview.Background = new SolidColorBrush(selectedColor); colorPreview.BorderBrush = Brushes.Gray; colorPreview.BorderThickness = new Thickness(1);
        var pick = new Button { Content = "选择…", Width = 110, Height = 32, Margin = new Thickness(16, 0, 0, 0) };
        pick.Click += (_, _) => PickColor();
        colorRow.Children.Add(colorPreview); colorRow.Children.Add(pick); Put(grid, colorRow, 1, 1);

        AddLabel(grid, "背景透明度", 2);
        var opacityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 13) };
        opacitySlider.Minimum = 10; opacitySlider.Maximum = 100; opacitySlider.Value = settings.OpacityPercent; opacitySlider.Width = 190; opacitySlider.TickFrequency = 5; opacitySlider.IsSnapToTickEnabled = true;
        opacityText.Width = 55; opacityText.Margin = new Thickness(12, 0, 0, 0); opacityText.VerticalAlignment = VerticalAlignment.Center;
        opacitySlider.ValueChanged += (_, _) => opacityText.Text = $"{opacitySlider.Value:0}%"; opacityText.Text = $"{opacitySlider.Value:0}%";
        opacityRow.Children.Add(opacitySlider); opacityRow.Children.Add(opacityText); Put(grid, opacityRow, 2, 1);

        topMost.Content = "始终置于其他窗口上方"; topMost.IsChecked = settings.AlwaysOnTop; Put(grid, topMost, 3, 1);
        foldersOnly.Content = "只显示文件夹"; foldersOnly.IsChecked = settings.ShowFoldersOnly; Put(grid, foldersOnly, 4, 1);
        hideDesktopFolders.Content = "从原桌面隐藏文件夹（可恢复）"; hideDesktopFolders.IsChecked = settings.HideFoldersOnDesktop; Put(grid, hideDesktopFolders, 5, 1);
        followTheme.Content = "背景颜色跟随 Windows 深色/浅色模式"; followTheme.IsChecked = settings.FollowSystemTheme; Put(grid, followTheme, 6, 1);
        followTheme.Checked += (_, _) => { colorPreview.Opacity = .45; pick.IsEnabled = false; };
        followTheme.Unchecked += (_, _) => { colorPreview.Opacity = 1; pick.IsEnabled = true; };
        if (settings.FollowSystemTheme) { colorPreview.Opacity = .45; pick.IsEnabled = false; }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 28, 0, 0) };
        var ok = new Button { Content = "确定", Width = 85, Height = 36, IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 85, Height = 36, Margin = new Thickness(12, 0, 0, 0), IsCancel = true };
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); Put(grid, buttons, 8, 1);
    }

    private void PickColor()
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(selectedColor.R, selectedColor.G, selectedColor.B),
            FullOpen = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        selectedColor = MediaColor.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
        colorPreview.Background = new SolidColorBrush(selectedColor);
    }

    private static void AddLabel(Grid grid, string text, int row) => Put(grid,
        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 15) }, row, 0);
    private static void Put(Grid grid, UIElement element, int row, int col)
    {
        Grid.SetRow(element, row); Grid.SetColumn(element, col); grid.Children.Add(element);
        if (element is WpfCheckBox check) check.Margin = new Thickness(0, 0, 0, 13);
    }
    private static MediaColor FromArgb(int argb) => MediaColor.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
