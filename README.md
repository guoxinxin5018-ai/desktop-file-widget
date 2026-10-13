# 桌面文件小组件

适用于 Windows 11 的透明桌面文件整理小组件。它读取系统桌面目录并分组显示文件，不替换 Windows 桌面，也不会移动或删除原文件。

## 功能

- 文件夹、普通文件、安装程序分组显示
- 自定义名称、背景颜色、透明度及窗口锁定
- Win11 圆角透明界面，图标和文字保持清晰
- 按名称、类型、修改时间或大小排序
- Ctrl 多选、Shift 连续多选，并联动 Windows 原桌面选择
- 多选后支持打开、剪切、复制、删除到回收站及属性
- 文件夹可从原桌面隐藏，并可恢复
- 安装、开机启动和卸载

## 下载

可在 GitHub Releases 下载 `安装_桌面文件小组件.exe`。

## 编译

需要 .NET 8 SDK 和 Windows 10/11：

```powershell
dotnet publish .\desktop_file_widget\DesktopFileWidget.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
dotnet publish .\desktop_file_widget_installer\DesktopFileWidgetInstaller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\installer-publish
```

## 说明

当前版本：3.8.0。项目仅支持 Windows。
