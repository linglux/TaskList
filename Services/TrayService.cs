using System.ComponentModel;
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
    private readonly List<(ToolStripItem Item, string Key)> _localizedItems = new();
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
        void AddMenuItem(string key, Action action)
        {
            var item = menu.Items.Add(LocalizationService.T(key), null, (_, _) => action());
            _localizedItems.Add((item, key));
        }
        AddMenuItem("显示主窗口", show);
        AddMenuItem("快速添加任务", quickAdd);
        AddMenuItem("设置", settings);
        menu.Items.Add(new ToolStripSeparator());
        _statusItem = new ToolStripMenuItem(viewModel.TrayStatusText) { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        AddMenuItem("退出 TaskList", exit);
        menu.Opening += (_, _) => RefreshStatus();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = LocalizationService.T("TaskList · 专注今天"),
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => show();
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left) show();
        };
        LocalizationService.Instance.PropertyChanged += OnLanguageChanged;
        RefreshStatus();
    }

    public void RefreshStatus()
        => _statusItem.Text = _viewModel.TrayStatusText;

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        foreach (var (item, key) in _localizedItems)
            item.Text = LocalizationService.T(key);
        _notifyIcon.Text = LocalizationService.T("TaskList · 专注今天");
        RefreshStatus();
    }

    public void ShowFirstTimeTip()
    {
        if (TaskRepository.GetSetting("TrayTipShown", "0") == "1") return;
        TaskRepository.SetSetting("TrayTipShown", "1");
        _notifyIcon.ShowBalloonTip(3200, LocalizationService.T("TaskList 已在后台运行"),
            LocalizationService.T("关闭或最小化都会缩到托盘，双击图标即可回来。"), WinForms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        LocalizationService.Instance.PropertyChanged -= OnLanguageChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private static Icon LoadPrimaryIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/AppIcon.ico", UriKind.Absolute))
            ?? throw new InvalidOperationException(LocalizationService.T("无法加载 TaskList 主图标。"));
        using var stream = resource.Stream;
        using var icon = new Icon(stream, SystemInformation.SmallIconSize);
        return (Icon)icon.Clone();
    }
}
