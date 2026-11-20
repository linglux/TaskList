using System.Collections.ObjectModel;
using System.Windows.Input;
using TaskList.Helpers;
using TaskList.Models;
using static TaskList.Services.LocalizationService;

namespace TaskList.ViewModels;

public enum EditorMode { None, Task, Group, Confirm }

public sealed class EditorViewModel : ObservableObject
{
    private EditorMode _mode = EditorMode.None;
    private string _header = string.Empty;
    private string _message = string.Empty;
    private object?[] _messageArguments = Array.Empty<object?>();
    private string _text = string.Empty;
    private string _notes = string.Empty;
    private TaskWeight _weight = TaskWeight.Light;
    private string _selectedColor = Palette.GroupColors[0];
    private string _saveText = "保存";
    private long _targetId;

    public EditorViewModel()
    {
        SaveCommand = new RelayCommand(_ => Save(), _ => !string.IsNullOrWhiteSpace(Text));
        CancelCommand = new RelayCommand(_ => Close());
        ConfirmCommand = new RelayCommand(_ => Confirm());
    }

    public Action<EditorViewModel>? SaveAction { get; private set; }
    public Action<EditorViewModel>? ConfirmAction { get; private set; }

    public ObservableCollection<GroupViewModel> Groups { get; } = new();
    public GroupViewModel? SelectedGroup { get; set; }
    public IReadOnlyList<string> Colors { get; } = Palette.GroupColors;

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ConfirmCommand { get; }

    public EditorMode Mode { get => _mode; private set => SetProperty(ref _mode, value); }
    public string Header { get => T(_header); private set => SetProperty(ref _header, value); }
    public string Message => T(_message, _messageArguments);
    public string SaveText { get => T(_saveText); private set => SetProperty(ref _saveText, value); }
    public long TargetId => _targetId;

    public string Text { get => _text; set => SetProperty(ref _text, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public TaskWeight Weight { get => _weight; set => SetProperty(ref _weight, value); }
    public string SelectedColor { get => _selectedColor; set => SetProperty(ref _selectedColor, value); }

    public bool IsOpen => Mode != EditorMode.None;
    public bool IsTaskMode => Mode == EditorMode.Task;
    public bool IsGroupMode => Mode == EditorMode.Group;
    public bool IsConfirmMode => Mode == EditorMode.Confirm;

    public void OpenTask(TaskViewModel? task, IEnumerable<GroupViewModel> groups, Action<EditorViewModel> onSave)
    {
        Groups.Clear();
        foreach (var g in groups) Groups.Add(g);

        _targetId = task?.Id ?? 0;
        Header = task is null ? "新建任务" : "编辑任务";
        SaveText = task is null ? "创建任务" : "保存修改";
        Text = task?.Title ?? string.Empty;
        Notes = task?.Notes ?? string.Empty;
        Weight = task?.Weight ?? TaskWeight.Light;
        SelectedGroup = task is null
            ? Groups.FirstOrDefault(g => g.IsInbox) ?? Groups.FirstOrDefault()
            : Groups.FirstOrDefault(g => g.Id == task.GroupId);
        SaveAction = onSave;
        ConfirmAction = null;
        SetMode(EditorMode.Task);
    }

    public void OpenGroup(string header, GroupViewModel? group, Action<EditorViewModel> onSave)
    {
        _targetId = group?.Id ?? 0;
        Header = header;
        SaveText = group is null ? "创建清单" : "保存";
        Text = group?.Name ?? string.Empty;
        SelectedColor = group?.Color ?? Palette.GroupColors[0];
        SaveAction = onSave;
        ConfirmAction = null;
        SetMode(EditorMode.Group);
    }

    public void OpenConfirm(string header, string message, long targetId, Action<EditorViewModel> onConfirm,
        params object?[] arguments)
    {
        _targetId = targetId;
        Header = header;
        _message = message;
        _messageArguments = arguments;
        OnPropertyChanged(nameof(Message));
        SaveText = "确认删除";
        ConfirmAction = onConfirm;
        SaveAction = null;
        SetMode(EditorMode.Confirm);
    }

    public void Close()
    {
        SaveAction = null;
        ConfirmAction = null;
        SetMode(EditorMode.None);
    }

    private void Save()
    {
        var action = SaveAction;
        if (action is null || string.IsNullOrWhiteSpace(Text)) return;
        var copy = this;
        Close();
        action(copy);
    }

    private void Confirm()
    {
        var action = ConfirmAction;
        if (action is null) return;
        var copy = this;
        Close();
        action(copy);
    }

    private void SetMode(EditorMode mode)
    {
        Mode = mode;
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(IsTaskMode));
        OnPropertyChanged(nameof(IsGroupMode));
        OnPropertyChanged(nameof(IsConfirmMode));
        if (mode != EditorMode.None && mode != EditorMode.Confirm)
            CommandManager.InvalidateRequerySuggested();
    }
}
