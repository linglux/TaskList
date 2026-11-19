using System.Globalization;
using System.Windows.Media;
using TaskList.Helpers;
using TaskList.Models;

namespace TaskList.ViewModels;

public sealed class TaskViewModel : ObservableObject
{
    private long _id;
    private long _groupId;
    private string _title = string.Empty;
    private string _notes = string.Empty;
    private TaskWeight _weight = TaskWeight.Light;
    private DateTime _createdAt;
    private DateTime? _completedAt;
    private string _groupName = string.Empty;
    private string _groupColor = "#6C63FF";
    private bool _isBusy;

    public TaskViewModel(TaskItem model) => CopyFrom(model);

    public long Id => _id;
    public long GroupId => _groupId;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string GroupName { get => _groupName; set => SetProperty(ref _groupName, value); }
    public string GroupColor { get => _groupColor; set => SetProperty(ref _groupColor, value); }
    public DateTime CreatedAt => _createdAt;
    public DateTime? CompletedAt => _completedAt;

    public TaskWeight Weight
    {
        get => _weight;
        set
        {
            if (!SetProperty(ref _weight, value)) return;
            OnPropertyChanged(nameof(WeightText));
            OnPropertyChanged(nameof(WeightForeground));
            OnPropertyChanged(nameof(WeightBackground));
        }
    }

    public string WeightText => Palette.Of(_weight).Label;
    public SolidColorBrush WeightForeground => Palette.Of(_weight).Foreground;
    public SolidColorBrush WeightBackground => Palette.Of(_weight).Background;
    public SolidColorBrush GroupBrush => Palette.BrushFromHex(_groupColor);

    public string CreatedText
    {
        get
        {
            if (_createdAt.Date == DateTime.Today) return "今天 " + _createdAt.ToString("HH:mm");
            if (_createdAt.Date == DateTime.Today.AddDays(-1)) return "昨天 " + _createdAt.ToString("HH:mm");
            return _createdAt.ToString("M月d日 HH:mm", CultureInfo.GetCultureInfo("zh-CN"));
        }
    }

    public string CompletedTimeText => _completedAt?.ToString("HH:mm") ?? string.Empty;

    public string MetaText => $"{_groupName} · 创建于 {CreatedText}";

    public void CopyFrom(TaskItem model)
    {
        _id = model.Id;
        _groupId = model.GroupId;
        _title = model.Title;
        _notes = model.Notes;
        _weight = model.Weight;
        _createdAt = model.CreatedAt;
        _completedAt = model.CompletedAt;
        _groupName = model.GroupName;
        _groupColor = model.GroupColor;
        RaiseAll();
    }

    public TaskItem ToModel() => new()
    {
        Id = _id,
        GroupId = _groupId,
        Title = _title,
        Notes = _notes,
        Weight = _weight,
        CreatedAt = _createdAt,
        CompletedAt = _completedAt,
        GroupName = _groupName,
        GroupColor = _groupColor
    };
}
