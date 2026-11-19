using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TaskList.ViewModels;

namespace TaskList.Views;

public partial class EditorOverlay : UserControl
{
    public EditorOverlay() => InitializeComponent();

    private void OnBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is EditorViewModel editor) editor.Close();
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (Visibility != Visibility.Visible) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (DataContext is EditorViewModel { IsGroupMode: true }) GroupNameBox.Focus();
            else TaskTitleBox.Focus();
        }), DispatcherPriority.Loaded);
    }
}
