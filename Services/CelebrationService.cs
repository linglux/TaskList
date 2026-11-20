using System.Windows;
using TaskList.Models;

namespace TaskList.Services;

/// <summary>把「完成仪式」动画从任务列表视图转发到主窗口的全局遮罩层。</summary>
public static class CelebrationService
{
    public static CelebrationEffect Effect { get; set; } = CelebrationEffect.Original;
    public static Func<TaskWeight, string, Point, Task>? PlayHandler { get; set; }
    public static event Action<LocalizedMessage>? PlaybackFailed;

    public static async Task PlayAsync(TaskWeight weight, string title, Point origin)
    {
        var handler = PlayHandler;
        if (handler is null) return;
        try { await handler(weight, title, origin); }
        catch (Exception ex) { PlaybackFailed?.Invoke(new LocalizedMessage("完成特效播放失败：{0}", ex.Message)); }
    }
}
