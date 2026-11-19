using System.Windows.Media;
using TaskList.Models;

namespace TaskList.Helpers;

public sealed record WeightInfo(
    string Label,
    SolidColorBrush Foreground,
    SolidColorBrush Background,
    Color Accent,
    string Glyph);

public static class Palette
{
    private static SolidColorBrush Make(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static readonly WeightInfo Light = new(
        "轻量", Make("#0284C7"), Make("#E0F2FE"),
        (Color)ColorConverter.ConvertFromString("#38BDF8"), "轻");

    public static readonly WeightInfo Medium = new(
        "中型", Make("#B45309"), Make("#FEF3C7"),
        (Color)ColorConverter.ConvertFromString("#F59E0B"), "中");

    public static readonly WeightInfo Heavy = new(
        "重型", Make("#BE123C"), Make("#FFE4E6"),
        (Color)ColorConverter.ConvertFromString("#F43F5E"), "重");

    public static WeightInfo Of(TaskWeight weight) => weight switch
    {
        TaskWeight.Medium => Medium,
        TaskWeight.Heavy => Heavy,
        _ => Light
    };

    public static SolidColorBrush BrushFromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Make("#6C63FF");
        try { return Make(hex); }
        catch (FormatException) { return Make("#6C63FF"); }
    }

    public static readonly string[] GroupColors =
    {
        "#6C63FF", "#38BDF8", "#34D399", "#F59E0B",
        "#F472B6", "#F87171", "#A78BFA", "#22D3EE"
    };
}
