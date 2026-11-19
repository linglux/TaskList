using System.Windows;
using System.Windows.Media;

namespace TaskList.Helpers;

public static class VisualTree
{
    public static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    public static T? FindNamed<T>(DependencyObject? root, string name) where T : DependencyObject
    {
        if (root is null) return null;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed && child is FrameworkElement fe && fe.Name == name)
                return typed;
            var deeper = FindNamed<T>(child, name);
            if (deeper is not null) return deeper;
        }
        return null;
    }
}
