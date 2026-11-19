using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TaskList.Helpers;
using TaskList.Models;
using TaskList.Services;
using TaskList.ViewModels;

namespace TaskList.Views;

public partial class HomeView : UserControl
{
    private MainViewModel? _viewModel;

    public HomeView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        _viewModel = vm;
        vm.FocusNewTaskRequested += FocusAddBox;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) _viewModel.FocusNewTaskRequested -= FocusAddBox;
        _viewModel = null;
    }

    public void FocusAddBox()
    {
        AddBox.Focus();
        AddBox.CaretIndex = AddBox.Text.Length;
    }

    private void OnAddBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _viewModel?.AddTaskCommand.Execute(null);
        e.Handled = true;
    }

    private async void OnTaskCheckClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.DataContext is not TaskViewModel task) return;
        if (DataContext is not MainViewModel vm || task.IsBusy) return;
        if (box.IsChecked != true)
        {
            box.IsChecked = true;
            return;
        }

        task.IsBusy = true;
        box.IsEnabled = false;

        var row = VisualTree.FindAncestor<ListBoxItem>(box);
        var root = VisualTree.FindNamed<Grid>(row, "RowRoot");
        var ripple = VisualTree.FindNamed<Ellipse>(row, "Ripple");
        var checkPath = VisualTree.FindNamed<Path>(row, "CheckPath");
        var strike = VisualTree.FindNamed<Rectangle>(row, "Strike");
        var title = VisualTree.FindNamed<TextBlock>(row, "TitleText");

        var window = Window.GetWindow(this);
        Point origin = new(160, 200);
        if (window is not null)
        {
            try { origin = box.TransformToAncestor(window).Transform(new Point(14, 14)); }
            catch (InvalidOperationException) { }
        }

        SoundService.Play(task.Weight);
        _ = CelebrationService.PlayAsync(task.Weight, task.Title, origin);

        int duration = task.Weight switch
        {
            TaskWeight.Light => 720,
            TaskWeight.Medium => 980,
            _ => 1120
        };

        if (root is not null)
            PlayRowAnimation(root, ripple, checkPath, strike, title, duration);

        await Task.Delay(duration);
        vm.Complete(task);
    }

    private static void PlayRowAnimation(
        Grid root, Ellipse? ripple, Path? checkPath, Rectangle? strike, TextBlock? title, int duration)
    {
        var storyboard = new Storyboard();
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };

        // 模板内的 Freezable 会被 WPF 冻结，必须新建变换对象才能参与动画。
        if (ripple is not null)
        {
            var rippleScale = new ScaleTransform(0.35, 0.35);
            ripple.RenderTransform = rippleScale;
            ripple.RenderTransformOrigin = new Point(0.5, 0.5);
            Add(storyboard, rippleScale, ScaleTransform.ScaleXProperty, 0.35, 2.8, 0, 520, easeOut);
            Add(storyboard, rippleScale, ScaleTransform.ScaleYProperty, 0.35, 2.8, 0, 520, easeOut);
            Add(storyboard, ripple, UIElement.OpacityProperty, 0.55, 0, 0, 620, easeOut);
        }

        if (checkPath is not null)
        {
            var checkScale = new ScaleTransform(0.2, 0.2);
            checkPath.RenderTransform = checkScale;
            checkPath.RenderTransformOrigin = new Point(0.5, 0.5);
            Add(storyboard, checkScale, ScaleTransform.ScaleXProperty, 0.2, 1, 40, 280, back);
            Add(storyboard, checkScale, ScaleTransform.ScaleYProperty, 0.2, 1, 40, 280, back);
        }

        if (strike is not null)
        {
            double width = Math.Max(title?.ActualWidth ?? 0, 40);
            Add(storyboard, strike, FrameworkElement.WidthProperty, 0, width, 170, 300, easeOut);
        }

        var group = new TransformGroup();
        var translate = new TranslateTransform();
        var scale = new ScaleTransform(1, 1);
        group.Children.Add(translate);
        group.Children.Add(scale);
        root.RenderTransform = group;
        root.RenderTransformOrigin = new Point(0.5, 0.5);

        int fadeStart = duration - 220;
        Add(storyboard, root, UIElement.OpacityProperty, 1, 0, fadeStart, 220, easeOut);
        Add(storyboard, translate, TranslateTransform.XProperty, 0, 42, fadeStart, 220, easeOut);
        Add(storyboard, scale, ScaleTransform.ScaleYProperty, 1, 0.86, fadeStart, 220, easeOut);

        storyboard.Begin();
    }

    private static void Add(
        Storyboard storyboard, DependencyObject target, DependencyProperty property,
        double from, double to, int delayMs, int durationMs, IEasingFunction? easing = null)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = easing
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }
}
