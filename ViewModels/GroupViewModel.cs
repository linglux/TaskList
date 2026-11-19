using System.Windows.Media;
using TaskList.Helpers;
using TaskList.Models;

namespace TaskList.ViewModels;

public sealed class GroupViewModel : ObservableObject
{
    private string _name = string.Empty;
    private string _color = "#6C63FF";
    private int _count;
    private bool _isSelected;

    public GroupViewModel(TaskGroup model)
    {
        Id = model.Id;
        IsInbox = model.IsInbox;
        _name = model.Name;
        _color = model.Color;
    }

    public long Id { get; }
    public bool IsInbox { get; }

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public int Count { get => _count; set => SetProperty(ref _count, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public string Color
    {
        get => _color;
        set
        {
            if (!SetProperty(ref _color, value)) return;
            OnPropertyChanged(nameof(ColorBrush));
        }
    }

    public SolidColorBrush ColorBrush => Palette.BrushFromHex(_color);

    public override string ToString() => Name;
}
