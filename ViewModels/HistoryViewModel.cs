using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TaskList.Data;
using TaskList.Models;
using static TaskList.Services.LocalizationService;

namespace TaskList.ViewModels;

public sealed class HistoryViewModel : ObservableObject
{
    private readonly Action _onTasksChanged;
    private readonly Action<string, string, long, Action<long>, object?[]> _confirmDelete;
    private readonly DispatcherTimer _copyTimer;
    private DateTime _currentDate = DateTime.Today;
    private DateTime _summaryDate = DateTime.Today.AddDays(-1);
    private bool _isSummaryVisible;
    private bool _hasTasks;
    private string _summaryText = string.Empty;
    private string _copyFeedback = string.Empty;

    public HistoryViewModel(Action onTasksChanged, Action<string, string, long, Action<long>, object?[]> confirmDelete)
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

    public string DateTitle => $"{FormatDate(_currentDate)} · {Culture.DateTimeFormat.GetDayName(_currentDate.DayOfWeek)}";

    public string RelativeLabel
    {
        get
        {
            var delta = (DateTime.Today - _currentDate.Date).Days;
            return delta switch
            {
                0 => T("今天"),
                1 => T("昨天"),
                2 => T("前天"),
                _ => T("{0} 天前", delta)
            };
        }
    }

    public string SummaryDateTitle => $"{FormatDate(_summaryDate)} · {Culture.DateTimeFormat.GetDayName(_summaryDate.DayOfWeek)}";

    private static string FormatDate(DateTime date) => date.ToString(T("yyyy年M月d日"), Culture);

    public bool HasTasks { get => _hasTasks; private set => SetProperty(ref _hasTasks, value); }
    public string StatsLine => Tasks.Count == 0
        ? T("这一天还没有完成的任务")
        : T("共 {0} 项   ·   轻量 {1} / 中型 {2} / 重型 {3}", Tasks.Count,
            Tasks.Count(t => t.Weight == TaskWeight.Light),
            Tasks.Count(t => t.Weight == TaskWeight.Medium),
            Tasks.Count(t => t.Weight == TaskWeight.Heavy));

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
    public string CopyFeedback { get => T(_copyFeedback); private set => SetProperty(ref _copyFeedback, value); }

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

        OnPropertyChanged(nameof(StatsLine));
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
        sb.AppendLine(T("工作日报 · {0}（{1}）", FormatDate(_summaryDate),
            Culture.DateTimeFormat.GetDayName(_summaryDate.DayOfWeek)));
        sb.AppendLine("--------------------------------");

        if (records.Count == 0)
        {
            sb.AppendLine(T("（这一天没有已完成的任务）"));
        }
        else
        {
            for (int i = 0; i < records.Count; i++)
            {
                var t = records[i];
                var weight = PaletteOf(t.Weight);
                var time = t.CompletedAt?.ToString("HH:mm", Culture) ?? "";
                var note = string.IsNullOrWhiteSpace(t.Notes) ? "" : $"  — {t.Notes.Replace("\r", " ").Replace("\n", " ")}";
                sb.AppendLine(T("{0}. [{1}] {2}（{3} · {4} 完成）{5}", i + 1, weight, t.Title,
                    GroupName(t.GroupId, t.GroupName), time, note));
            }
        }

        int light = records.Count(t => t.Weight == TaskWeight.Light);
        int medium = records.Count(t => t.Weight == TaskWeight.Medium);
        int heavy = records.Count(t => t.Weight == TaskWeight.Heavy);
        sb.AppendLine("--------------------------------");
        sb.Append(T("合计 {0} 项：轻量 {1} / 中型 {2} / 重型 {3}", records.Count, light, medium, heavy));
        SummaryText = sb.ToString();
        NotifyDateProperties();
    }

    private static string PaletteOf(TaskWeight weight) => T(Helpers.Palette.Of(weight).Label);

    protected override void OnLanguageChanged()
    {
        if (_isSummaryVisible) BuildSummary();
        base.OnLanguageChanged();
    }

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
            "确定删除「{0}」的完成记录吗？删除后无法恢复。",
            task.Id,
            id =>
            {
                TaskRepository.DeleteTask(id);
                _onTasksChanged();
            }, new object?[] { task.Title });
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
