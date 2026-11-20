using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TaskList.Data;
using TaskList.Models;
using TaskList.Services;
using static TaskList.Services.LocalizationService;

namespace TaskList.ViewModels;

public enum AppSection { Today, All, Group, History, AllGroups, Settings }

public sealed class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _toastTimer;

    private AppSection _section = AppSection.Today;
    private GroupViewModel? _selectedGroup;
    private string _newTaskTitle = string.Empty;
    private TaskWeight _newTaskWeight = TaskWeight.Light;
    private GroupViewModel? _newTaskGroup;
    private bool _hasTasks;

    private int _todayCompleted;
    private int _weekCompleted;
    private int _openTasks;
    private int _streakDays;
    private string _toastText = string.Empty;
    private object?[] _toastArguments = Array.Empty<object?>();
    private bool _isToastVisible;
    private bool _canUndoToast;
    private long _lastCompletedId;

    public MainViewModel()
    {
        History = new HistoryViewModel(RefreshAll, ConfirmHistoryDelete);
        Editor = new EditorViewModel();

        Settings = new SettingsViewModel();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _toastTimer.Tick += (_, _) => { IsToastVisible = false; _toastTimer.Stop(); };

        NavigateTodayCommand = new RelayCommand(_ => NavigateToday());
        NavigateAllCommand = new RelayCommand(_ => NavigateAll());
        NavigateHistoryCommand = new RelayCommand(_ => NavigateHistory());
        SelectAllGroupsCommand = new RelayCommand(_ => SelectAllGroups());
        SelectGroupCommand = new RelayCommand(p => { if (p is GroupViewModel g) SelectGroup(g); });
        AddTaskCommand = new RelayCommand(_ => AddTask(), _ => !string.IsNullOrWhiteSpace(_newTaskTitle));
        NewTaskDetailedCommand = new RelayCommand(_ => OpenNewTaskEditor());
        AddGroupCommand = new RelayCommand(_ => OpenNewGroupEditor());
        RenameGroupCommand = new RelayCommand(p => { if (p is GroupViewModel g) OpenRenameGroupEditor(g); });
        DeleteGroupCommand = new RelayCommand(p => { if (p is GroupViewModel g && !g.IsInbox) OpenDeleteGroupEditor(g); });
        EditTaskCommand = new RelayCommand(p => { if (p is TaskViewModel t) OpenEditTaskEditor(t); });
        DeleteTaskCommand = new RelayCommand(p => { if (p is TaskViewModel t) OpenDeleteTaskEditor(t); });
        UndoCommand = new RelayCommand(_ => UndoComplete(), _ => _canUndoToast);
        DismissToastCommand = new RelayCommand(_ => IsToastVisible = false);
        NavigateSettingsCommand = new RelayCommand(_ => SetSection(AppSection.Settings, null));

        ReloadGroups();
        LoadTasks();
        RefreshStats();
    }

    public event Action? FocusNewTaskRequested;

    public ObservableCollection<GroupViewModel> Groups { get; } = new();
    public ObservableCollection<TaskViewModel> Tasks { get; } = new();
    public HistoryViewModel History { get; }
    public EditorViewModel Editor { get; }
    public SettingsViewModel Settings { get; }

    public ICommand NavigateTodayCommand { get; }
    public ICommand NavigateAllCommand { get; }
    public ICommand NavigateHistoryCommand { get; }
    public ICommand SelectAllGroupsCommand { get; }
    public ICommand SelectGroupCommand { get; }
    public ICommand AddTaskCommand { get; }
    public ICommand NewTaskDetailedCommand { get; }
    public ICommand AddGroupCommand { get; }
    public ICommand RenameGroupCommand { get; }
    public ICommand DeleteGroupCommand { get; }
    public ICommand EditTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand DismissToastCommand { get; }
    public ICommand NavigateSettingsCommand { get; }

    public object CurrentView => _section switch
    {
        AppSection.History => History,
        AppSection.Settings => Settings,
        _ => this
    };
    public bool IsTodayActive => _section == AppSection.Today;
    public bool IsAllActive => _section == AppSection.All;
    public bool IsAllGroupsActive => _section == AppSection.AllGroups;
    public bool IsHistoryActive => _section == AppSection.History;
    public bool IsSettingsActive => _section == AppSection.Settings;
    public bool IsHomeSection => _section is not (AppSection.History or AppSection.Settings);
    public bool HasTasks { get => _hasTasks; private set => SetProperty(ref _hasTasks, value); }

    public string HeaderTitle => _section switch
    {
        AppSection.Today => T("今天"),
        AppSection.All => T("全部任务"),
        AppSection.AllGroups => T("所有清单"),
        AppSection.Group => _selectedGroup?.Name ?? T("我的清单"),
        AppSection.Settings => T("设置"),
        _ => T("历史记录")
    };

    public string HeaderSubtitle => _section switch
    {
        AppSection.Today => T("今天创建的待办 · 一件一件，慢慢来"),
        AppSection.All => T("所有清单中尚未完成的任务"),
        AppSection.AllGroups => T("汇总每个清单里尚未完成的任务"),
        AppSection.Group => T("这个清单里的待办任务"),
        AppSection.Settings => T("个性化音效与完成特效"),
        _ => T("按天回看已经完成的任务")
    };

    public string EmptyHint => _section switch
    {
        AppSection.Today => T("今天还没有新建任务，从上面添加第一件吧"),
        AppSection.Group => T("这个清单是空的，添加一件想做的事"),
        AppSection.AllGroups => T("所有清单里都没有待办，享受这份清爽"),
        _ => T("没有未完成的任务，享受这份清爽")
    };

    public string NewTaskTitle
    {
        get => _newTaskTitle;
        set
        {
            if (!SetProperty(ref _newTaskTitle, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public TaskWeight NewTaskWeight
    {
        get => _newTaskWeight;
        set => SetProperty(ref _newTaskWeight, value);
    }

    public GroupViewModel? NewTaskGroup
    {
        get => _newTaskGroup;
        set => SetProperty(ref _newTaskGroup, value);
    }

    public int TodayCompleted { get => _todayCompleted; private set => SetProperty(ref _todayCompleted, value); }
    public int WeekCompleted { get => _weekCompleted; private set => SetProperty(ref _weekCompleted, value); }
    public int OpenTasks { get => _openTasks; private set => SetProperty(ref _openTasks, value); }
    public int StreakDays { get => _streakDays; private set => SetProperty(ref _streakDays, value); }

    public string StatsLine => T("今日完成 {0}   ·   本周 {1}   ·   连续 {2} 天", _todayCompleted, _weekCompleted, _streakDays);
    public string TrayStatusText => T("今日已完成 {0} 项 · 待办 {1} 项", _todayCompleted, _openTasks);

    public string ToastText => T(_toastText, _toastArguments);
    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }
    public bool CanUndoToast
    {
        get => _canUndoToast;
        private set
        {
            if (!SetProperty(ref _canUndoToast, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    // ---------- 导航 ----------

    private void NavigateToday() => SetSection(AppSection.Today, null);
    private void NavigateAll() => SetSection(AppSection.All, null);
    private void NavigateHistory()
    {
        SetSection(AppSection.History, null);
        History.LoadLatest();
    }

    private void SelectGroup(GroupViewModel group) => SetSection(AppSection.Group, group);
    private void SelectAllGroups() => SetSection(AppSection.AllGroups, null);

    private void SetSection(AppSection section, GroupViewModel? group)
    {
        if (_section == AppSection.Settings && section != AppSection.Settings) Settings.Deactivate();
        _section = section;
        _selectedGroup = group;
        foreach (var g in Groups) g.IsSelected = group is not null && g.Id == group.Id;
        NewTaskGroup = group ?? Groups.FirstOrDefault(g => g.IsInbox);
        if (IsHomeSection) LoadTasks();
        NotifyNavigation();
    }

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(CurrentView));
        OnPropertyChanged(nameof(IsTodayActive));
        OnPropertyChanged(nameof(IsAllActive));
        OnPropertyChanged(nameof(IsAllGroupsActive));
        OnPropertyChanged(nameof(IsHistoryActive));
        OnPropertyChanged(nameof(IsSettingsActive));
        OnPropertyChanged(nameof(IsHomeSection));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(EmptyHint));
    }

    // ---------- 数据加载 ----------

    public void RefreshAll()
    {
        ReloadGroups();
        LoadTasks();
        RefreshStats();
        if (_section == AppSection.History) History.Refresh();
    }

    private void ReloadGroups()
    {
        var records = TaskRepository.GetGroups();
        var counts = TaskRepository.GetOpenCounts();
        var keepId = _selectedGroup?.Id;
        Groups.Clear();
        foreach (var record in records)
        {
            Groups.Add(new GroupViewModel(record)
            {
                Count = counts.TryGetValue(record.Id, out var n) ? n : 0
            });
        }
        _selectedGroup = keepId.HasValue ? Groups.FirstOrDefault(g => g.Id == keepId.Value) : null;
        foreach (var g in Groups) g.IsSelected = _selectedGroup is not null && g.Id == _selectedGroup.Id;
        if (_section == AppSection.Group && _selectedGroup is null) _section = AppSection.Today;

        NewTaskGroup = _selectedGroup ?? Groups.FirstOrDefault(g => g.IsInbox);
        NotifyNavigation();
    }

    private void LoadTasks()
    {
        long? groupId = _section == AppSection.Group ? _selectedGroup?.Id : null;
        bool todayOnly = _section == AppSection.Today;
        var records = TaskRepository.GetOpenTasks(groupId, todayOnly);
        Tasks.Clear();
        foreach (var record in records) Tasks.Add(new TaskViewModel(record));
        HasTasks = Tasks.Count > 0;
        OnPropertyChanged(nameof(EmptyHint));
    }

    private void RefreshStats()
    {
        var stats = TaskRepository.GetStats();
        TodayCompleted = stats.TodayCompleted;
        WeekCompleted = stats.WeekCompleted;
        OpenTasks = stats.OpenTasks;
        StreakDays = stats.StreakDays;
        OnPropertyChanged(nameof(StatsLine));
        OnPropertyChanged(nameof(TrayStatusText));
    }

    private bool BelongsToCurrentView(TaskItem item)
    {
        if (item.IsCompleted) return false;
        return _section switch
        {
            AppSection.Group => _selectedGroup is not null && item.GroupId == _selectedGroup.Id,
            AppSection.Today => item.CreatedAt.Date == DateTime.Today,
            AppSection.All => true,
            AppSection.AllGroups => true,
            _ => false
        };
    }

    // ---------- 任务操作 ----------

    private void AddTask()
    {
        var title = NewTaskTitle.Trim();
        if (title.Length == 0) return;
        var group = NewTaskGroup ?? Groups.FirstOrDefault(g => g.IsInbox) ?? Groups.FirstOrDefault();
        if (group is null) return;

        var id = TaskRepository.AddTask(new TaskItem
        {
            GroupId = group.Id,
            Title = title,
            Notes = string.Empty,
            Weight = NewTaskWeight,
            CreatedAt = DateTime.Now
        });

        NewTaskTitle = string.Empty;
        NewTaskWeight = TaskWeight.Light;

        var created = TaskRepository.GetTask(id);
        if (created is not null && BelongsToCurrentView(created))
            Tasks.Insert(0, new TaskViewModel(created));

        HasTasks = Tasks.Count > 0;
        RefreshStats();
        RefreshGroupCounts();
        FocusNewTaskRequested?.Invoke();
    }

    public void Complete(TaskViewModel task)
    {
        var now = DateTime.Now;
        TaskRepository.CompleteTask(task.Id, now);
        _lastCompletedId = task.Id;
        Tasks.Remove(task);
        HasTasks = Tasks.Count > 0;
        RefreshStats();
        RefreshGroupCounts();
        if (_section == AppSection.History) History.Refresh();
        ShowToast("已完成「{0}」", true, task.Title);
    }

    private void UndoComplete()
    {
        if (_lastCompletedId <= 0) return;
        TaskRepository.ReopenTask(_lastCompletedId);
        var item = TaskRepository.GetTask(_lastCompletedId);
        if (item is not null && BelongsToCurrentView(item) && Tasks.All(t => t.Id != item.Id))
            Tasks.Insert(0, new TaskViewModel(item));
        _lastCompletedId = 0;
        HasTasks = Tasks.Count > 0;
        IsToastVisible = false;
        CanUndoToast = false;
        RefreshStats();
        RefreshGroupCounts();
        if (_section == AppSection.History) History.Refresh();
    }

    private void RefreshGroupCounts()
    {
        var counts = TaskRepository.GetOpenCounts();
        foreach (var g in Groups) g.Count = counts.TryGetValue(g.Id, out var n) ? n : 0;
    }

    private void ShowToast(string text, bool canUndo, params object?[] arguments)
    {
        _toastText = text;
        _toastArguments = arguments;
        OnPropertyChanged(nameof(ToastText));
        CanUndoToast = canUndo;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public void QuickAdd()
    {
        if (!IsHomeSection) SetSection(AppSection.All, null);
        Application.Current?.Dispatcher.BeginInvoke(
            new Action(() => FocusNewTaskRequested?.Invoke()), DispatcherPriority.Loaded);
    }

    // ---------- 编辑器 ----------

    private void OpenNewTaskEditor()
    {
        Editor.OpenTask(null, Groups, e => CreateDetailedTask(e));
    }

    private void OpenEditTaskEditor(TaskViewModel task)
    {
        Editor.OpenTask(task, Groups, e =>
        {
            var model = TaskRepository.GetTask(e.TargetId);
            if (model is null) return;
            model.Title = e.Text.Trim();
            model.Notes = e.Notes.Trim();
            model.Weight = e.Weight;
            if (e.SelectedGroup is not null) model.GroupId = e.SelectedGroup.Id;
            TaskRepository.UpdateTask(model);
            RefreshAll();
        });
    }

    private void CreateDetailedTask(EditorViewModel e)
    {
        var group = e.SelectedGroup ?? Groups.FirstOrDefault(g => g.IsInbox);
        if (group is null || string.IsNullOrWhiteSpace(e.Text)) return;
        TaskRepository.AddTask(new TaskItem
        {
            GroupId = group.Id,
            Title = e.Text.Trim(),
            Notes = e.Notes.Trim(),
            Weight = e.Weight,
            CreatedAt = DateTime.Now
        });
        RefreshAll();
    }

    private void OpenDeleteTaskEditor(TaskViewModel task)
    {
        Editor.OpenConfirm("删除任务", "确定删除「{0}」吗？删除后无法恢复。", task.Id, e =>
        {
            TaskRepository.DeleteTask(e.TargetId);
            RefreshAll();
        }, task.Title);
    }

    private void ConfirmHistoryDelete(string header, string message, long id, Action<long> onConfirm, object?[] arguments)
        => Editor.OpenConfirm(header, message, id, e => onConfirm(e.TargetId), arguments);

    private void OpenNewGroupEditor()
    {
        Editor.OpenGroup("新建清单", null, e =>
        {
            TaskRepository.AddGroup(e.Text.Trim(), e.SelectedColor);
            RefreshAll();
        });
    }

    private void OpenRenameGroupEditor(GroupViewModel group)
    {
        Editor.OpenGroup("编辑清单", group, e =>
        {
            TaskRepository.RenameGroup(e.TargetId, e.Text.Trim(), e.SelectedColor);
            RefreshAll();
        });
    }

    private void OpenDeleteGroupEditor(GroupViewModel group)
    {
        Editor.OpenConfirm("删除清单",
            "确定删除清单「{0}」吗？其中的任务会移动到收件箱，不会丢失。",
            group.Id, e =>
            {
                TaskRepository.DeleteGroup(e.TargetId);
                if (_selectedGroup?.Id == e.TargetId) _selectedGroup = null;
                SetSection(AppSection.All, null);
                RefreshAll();
            }, group.Name);
    }
}
