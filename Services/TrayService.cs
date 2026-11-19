using System.Drawing;
using System.Windows.Forms;
using TaskList.Data;
using TaskList.ViewModels;
using WinForms = System.Windows.Forms;

namespace TaskList.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly MainViewModel _viewModel;
    private readonly ToolStripMenuItem _statusItem;
    private bool _disposed;

    public TrayService(MainViewModel viewModel, Action show, Action quickAdd, Action settings, Action exit)
    {
        _viewModel = viewModel;
        _icon = LoadPrimaryIcon();

        var menu = new ContextMenuStrip
        {
            Font = new System.Drawing.Font("Microsoft YaHei UI", 9F),
            Padding = new Padding(4),
            RenderMode = ToolStripRenderMode.System
        };
        menu.Items.Add("显示主窗口", null, (_, _) => show());
        menu.Items.Add("快速添加任务", null, (_, _) => quickAdd());
        menu.Items.Add("设置", null, (_, _) => settings());
        menu.Items.Add(new ToolStripSeparator());
        _statusItem = new ToolStripMenuItem(viewModel.TrayStatusText) { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出 TaskList", null, (_, _) => exit());
        menu.Opening += (_, _) => RefreshStatus();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "TaskList · 专注今天",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => show();
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left) show();
        };
        RefreshStatus();
    }

    public void RefreshStatus()
        => _statusItem.Text = _viewModel.TrayStatusText;

    public void ShowFirstTimeTip()
    {
        if (TaskRepository.GetSetting("TrayTipShown", "0") == "1") return;
        TaskRepository.SetSetting("TrayTipShown", "1");
        _notifyIcon.ShowBalloonTip(3200, "TaskList 已在后台运行",
            "关闭或最小化都会缩到托盘，双击图标即可回来。", WinForms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private static Icon LoadPrimaryIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/AppIcon.ico", UriKind.Absolute))
            ?? throw new InvalidOperationException("无法加载 TaskList 主图标。");
        using var stream = resource.Stream;
        using var icon = new Icon(stream, SystemInformation.SmallIconSize);
        return (Icon)icon.Clone();
    }
}
