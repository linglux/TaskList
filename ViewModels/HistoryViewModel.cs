using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TaskList.Data;
using TaskList.Models;
using TaskList.ViewModels;

namespace TaskList.ViewModels;

public sealed class HistoryViewModel : ObservableObject
{
    private static readonly CultureInfo Zh = CultureInfo.GetCultureInfo("zh-CN");

    private readonly Action _onTasksChanged;
    private readonly Action<string, string, long, Action<long>> _confirmDelete;
    private readonly DispatcherTimer _copyTimer;
    private DateTime _currentDate = DateTime.Today;
    private DateTime _summaryDate = DateTime.Today.AddDays(-1);
    private bool _isSummaryVisible;
    private bool _hasTasks;
    private string _statsLine = string.Empty;
    private string _summaryText = string.Empty;
    private string _copyFeedback = string.Empty;

    public HistoryViewModel(Action onTasksChanged, Action<string, string, long, Action<long>> confirmDelete)
    {
        _onTasksChanged = onTasksChanged;
        _confirmDelete = confirmDelete;
        _copyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _copyTimer.Tick += (_, _) => { CopyFeedback = string.Empty; _copyTimer.Stop(); };

        PrevDayCommand = new RelayCommand(_ => Shift(-1), _ => true);
        NextDayCommand = new RelayCommand(_ => Shift(1), _ => _currentDate.Date < DateTime.Today);
        LatestCommand = new RelayCommand(_ => LoadLatest());
        SummaryCommand = new RelayCommand(_ => OpenSummary());
        PrevSummaryDayCommand = new RelayCommand(_ => ShiftSummary(-1));
        NextSummaryDayCommand = new RelayCommand(_ => ShiftSummary(1), _ => _summaryDate.Date < DateTime.Today);
        CopyCommand = new RelayCommand(_ => Copy(), _ => IsSummaryVisible);
        CloseSummaryCommand = new RelayCommand(_ => IsSummaryVisible = false);
        ReopenCommand = new RelayCommand(p => Reopen(p));
        DeleteCommand = new RelayCommand(p => Delete(p));
    }

    public ObservableCollection<TaskViewModel> Tasks { get; } = new();

    public ICommand PrevDayCommand { get; }
    public ICommand NextDayCommand { get; }
    public ICommand LatestCommand { get; }
    public ICommand SummaryCommand { get; }
    public ICommand PrevSummaryDayCommand { get; }
    public ICommand NextSummaryDayCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand CloseSummaryCommand { get; }
    public ICommand ReopenCommand { get; }
    public ICommand DeleteCommand { get; }

    public DateTime CurrentDate => _currentDate;
    public DateTime SummaryDate => _summaryDate;

    public string DateTitle => $"{_currentDate:yyyy年M月d日} · {Zh.DateTimeFormat.GetDayName(_currentDate.DayOfWeek)}";

    public string RelativeLabel
    {
        get
        {
            var delta = (DateTime.Today - _currentDate.Date).Days;
            return delta switch
            {
                0 => "今天",
                1 => "昨天",
                2 => "前天",
                _ => $"{delta} 天前"
            };
        }
    }

    public string SummaryDateTitle => $"{_summaryDate:yyyy年M月d日} · {Zh.DateTimeFormat.GetDayName(_summaryDate.DayOfWeek)}";

    public bool HasTasks { get => _hasTasks; private set => SetProperty(ref _hasTasks, value); }
    public string StatsLine { get => _statsLine; private set => SetProperty(ref _statsLine, value); }

    public bool IsSummaryVisible
    {
        get => _isSummaryVisible;
        set
        {
            if (!SetProperty(ref _isSummaryVisible, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }
    public string CopyFeedback { get => _copyFeedback; private set => SetProperty(ref _copyFeedback, value); }

    public void LoadLatest()
    {
        var dates = TaskRepository.GetCompletedDates();
        SetDate(dates.Count > 0 ? dates[0] : DateTime.Today, resetSummary: true);
    }

    public void Refresh()
    {
        LoadDay();
        if (_isSummaryVisible) BuildSummary();
    }

    private void Shift(int delta)
    {
        var target = _currentDate.AddDays(delta);
        if (target.Date > DateTime.Today) return;
        SetDate(target, resetSummary: true);
    }

    private void SetDate(DateTime date, bool resetSummary)
    {
        _currentDate = date.Date;
        LoadDay();
        if (resetSummary) _summaryDate = _currentDate.AddDays(-1);
        else if (_isSummaryVisible) _summaryDate = _currentDate.AddDays(-1);
        if (_isSummaryVisible) BuildSummary();
        NotifyDateProperties();
    }

    private void LoadDay()
    {
        var records = TaskRepository.GetCompletedOn(_currentDate);
        Tasks.Clear();
        foreach (var item in records) Tasks.Add(new TaskViewModel(item));
        HasTasks = Tasks.Count > 0;

        int light = Tasks.Count(t => t.Weight == TaskWeight.Light);
        int medium = Tasks.Count(t => t.Weight == TaskWeight.Medium);
        int heavy = Tasks.Count(t => t.Weight == TaskWeight.Heavy);
        StatsLine = Tasks.Count == 0
            ? "这一天还没有完成的任务"
            : $"共 {Tasks.Count} 项   ·   轻量 {light} / 中型 {medium} / 重型 {heavy}";
    }

    private void OpenSummary()
    {
        _summaryDate = _currentDate.AddDays(-1);
        IsSummaryVisible = true;
        BuildSummary();
    }

    private void ShiftSummary(int delta)
    {
        var target = _summaryDate.AddDays(delta);
        if (target.Date > DateTime.Today) return;
        _summaryDate = target;
        BuildSummary();
        NotifyDateProperties();
    }

    private void BuildSummary()
    {
        var records = TaskRepository.GetCompletedOn(_summaryDate);
        var sb = new StringBuilder();
        sb.AppendLine($"工作日报 · {_summaryDate:yyyy年M月d日}（{Zh.DateTimeFormat.GetDayName(_summaryDate.DayOfWeek)}）");
        sb.AppendLine("--------------------------------");

        if (records.Count == 0)
        {
            sb.AppendLine("（这一天没有已完成的任务）");
        }
        else
        {
            for (int i = 0; i < records.Count; i++)
            {
                var t = records[i];
                var weight = PaletteOf(t.Weight);
                var time = t.CompletedAt?.ToString("HH:mm") ?? "";
                var note = string.IsNullOrWhiteSpace(t.Notes) ? "" : $"  — {t.Notes.Replace("\r", " ").Replace("\n", " ")}";
                sb.AppendLine($"{i + 1}. [{weight}] {t.Title}（{t.GroupName} · {time} 完成）{note}");
            }
        }

        int light = records.Count(t => t.Weight == TaskWeight.Light);
        int medium = records.Count(t => t.Weight == TaskWeight.Medium);
        int heavy = records.Count(t => t.Weight == TaskWeight.Heavy);
        sb.AppendLine("--------------------------------");
        sb.Append($"合计 {records.Count} 项：轻量 {light} / 中型 {medium} / 重型 {heavy}");
        SummaryText = sb.ToString();
        NotifyDateProperties();
    }

    private static string PaletteOf(TaskWeight weight) => Helpers.Palette.Of(weight).Label;

    private void Copy()
    {
        if (string.IsNullOrEmpty(SummaryText)) return;
        Clipboard.SetText(SummaryText);
        CopyFeedback = "已复制到剪贴板";
        _copyTimer.Stop();
        _copyTimer.Start();
    }

    private void Reopen(object? parameter)
    {
        if (parameter is not TaskViewModel task) return;
        TaskRepository.ReopenTask(task.Id);
        LoadDay();
        _onTasksChanged();
    }

    private void Delete(object? parameter)
    {
        if (parameter is not TaskViewModel task) return;
        _confirmDelete("删除完成记录",
            $"确定删除「{task.Title}」的完成记录吗？删除后无法恢复。",
            task.Id,
            id =>
            {
                TaskRepository.DeleteTask(id);
                _onTasksChanged();
            });
    }

    private void NotifyDateProperties()
    {
        OnPropertyChanged(nameof(DateTitle));
        OnPropertyChanged(nameof(RelativeLabel));
        OnPropertyChanged(nameof(SummaryDateTitle));
        OnPropertyChanged(nameof(CurrentDate));
        OnPropertyChanged(nameof(SummaryDate));
        CommandManager.InvalidateRequerySuggested();
    }
}
