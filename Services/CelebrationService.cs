using System.Windows;
using TaskList.Models;

namespace TaskList.Services;

/// <summary>把「完成仪式」动画从任务列表视图转发到主窗口的全局遮罩层。</summary>
public static class CelebrationService
{
    public static Func<TaskWeight, string, Point, Task>? PlayHandler { get; set; }

    public static Task PlayAsync(TaskWeight weight, string title, Point origin)
    {
        var handler = PlayHandler;
        return handler is null ? Task.CompletedTask : handler(weight, title, origin);
    }
}
