namespace TaskList.Models;

public enum TaskWeight
{
    Light = 0,
    Medium = 1,
    Heavy = 2
}

public enum CelebrationEffect
{
    Original = 0,
    Applause = 1,
    MinimalCheck = 2,
    CompletionStamp = 3,
    Starlight = 4,
    Fireworks = 5
}

public sealed class TaskItem
{
    public long Id { get; set; }
    public long GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public TaskWeight Weight { get; set; } = TaskWeight.Light;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int SortOrder { get; set; }

    public string GroupName { get; set; } = string.Empty;
    public string GroupColor { get; set; } = "#6C63FF";

    public bool IsCompleted => CompletedAt.HasValue;
}

public sealed class TaskGroup
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#6C63FF";
    public bool IsInbox { get; set; }
    public int SortOrder { get; set; }
}

public sealed record TaskStats(int TodayCompleted, int WeekCompleted, int OpenTasks, int StreakDays);
