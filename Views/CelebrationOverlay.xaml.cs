using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TaskList.Models;

namespace TaskList.Views;

public partial class CelebrationOverlay : UserControl
{
    private sealed class Particle
    {
        public required Shape Shape { get; init; }
        public Vector Velocity { get; set; }
        public double Gravity { get; init; }
        public double Life { get; set; }
        public double MaxLife { get; init; }
        public required RotateTransform Rotate { get; init; }
        public double Spin { get; init; }
    }

    private static readonly Color[] ConfettiColors =
    {
        Color.FromRgb(0x6C, 0x63, 0xFF),
        Color.FromRgb(0x38, 0xBD, 0xF8),
        Color.FromRgb(0x34, 0xD3, 0x99),
        Color.FromRgb(0xFB, 0xBF, 0x24),
        Color.FromRgb(0xF4, 0x72, 0xB6),
        Color.FromRgb(0xFF, 0xFF, 0xFF)
    };

    private static readonly string[] Cheers =
    {
        "这一块硬骨头，被你啃下来了。",
        "郑重记上一笔：今天很了不起。",
        "重量级完成，值得为自己鼓掌。",
        "又跨过了一座山，继续向前。",
        "专注了这么久，这一刻属于你。"
    };

    private static readonly string[] MediumHints =
    {
        "稳稳推进了一步",
        "这一步，很扎实",
        "节奏保持得不错",
        "又拿下了一块",
        "继续，状态正好"
    };

    private readonly List<Particle> _particles = new();
    private readonly List<(DependencyObject Target, DependencyProperty Property)> _animated = new();
    private readonly Random _random = new();
    private bool _busy;
    private bool _rendering;
    private DateTime _lastFrame;

    public CelebrationOverlay() => InitializeComponent();

    public async Task PlayAsync(TaskWeight weight, string title, Point origin)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            Visibility = Visibility.Visible;
            await Task.Delay(20);
            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            switch (weight)
            {
                case TaskWeight.Light:
                    await PlayLightAsync(origin);
                    break;
                case TaskWeight.Medium:
                    await PlayMediumAsync(title, origin);
                    break;
                default:
                    await PlayHeavyAsync(title);
                    break;
            }
        }
        finally
        {
            StopRendering();
            Reset();
            Visibility = Visibility.Collapsed;
            _busy = false;
        }
    }

    // 轻型：纸屑散落 + 浮字，界面不被遮挡（原中型效果）
    private async Task PlayLightAsync(Point origin)
    {
        Spawn(origin, 48, radial: false, gravity: 620);
        StartRendering();

        FloatingLabel.Visibility = Visibility.Visible;
        FloatingLabel.Opacity = 1;
        Canvas.SetLeft(FloatingLabel, origin.X - 24);
        Canvas.SetTop(FloatingLabel, origin.Y - 28);
        var floatTranslate = new TranslateTransform();
        FloatingLabel.RenderTransform = floatTranslate;
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Animate(floatTranslate, TranslateTransform.YProperty, 0, -76, 0, 900, ease);
            Animate(FloatingLabel, UIElement.OpacityProperty, 1, 0, 260, 640, ease);
        }

        await Task.Delay(1250);
        FloatingLabel.Visibility = Visibility.Collapsed;
    }

    // 中型：局部光晕 + 光环脉冲 + 成就卡片 + 更多彩带，仍不遮挡界面
    private async Task PlayMediumAsync(string title, Point origin)
    {
        const double cardWidth = 268;
        const double cardHeight = 130;
        double left = Math.Clamp(origin.X - 12, 16, Math.Max(16, ActualWidth - cardWidth - 16));
        double top = Math.Clamp(origin.Y - 24, 16, Math.Max(16, ActualHeight - cardHeight - 16));
        var center = new Point(left + cardWidth / 2, top + cardHeight / 2);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };

        MediumTaskTitle.Text = title.Length > 34 ? title[..34] + "…" : title;
        MediumHint.Text = MediumHints[_random.Next(MediumHints.Length)];

        MediumGlow.Visibility = Visibility.Visible;
        Canvas.SetLeft(MediumGlow, center.X - 75);
        Canvas.SetTop(MediumGlow, center.Y - 75);
        var glowScale = new ScaleTransform(0.5, 0.5);
        MediumGlow.RenderTransform = glowScale;
        Animate(MediumGlow, UIElement.OpacityProperty, 0, 0.85, 0, 240, ease);
        Animate(MediumGlow, UIElement.OpacityProperty, 0.85, 0, 560, 620, ease);
        Animate(glowScale, ScaleTransform.ScaleXProperty, 0.5, 2.1, 0, 1100, ease);
        Animate(glowScale, ScaleTransform.ScaleYProperty, 0.5, 2.1, 0, 1100, ease);

        MediumRing.Visibility = Visibility.Visible;
        Canvas.SetLeft(MediumRing, center.X - 48);
        Canvas.SetTop(MediumRing, center.Y - 48);
        var ringScale = new ScaleTransform(0.3, 0.3);
        MediumRing.RenderTransform = ringScale;
        Animate(ringScale, ScaleTransform.ScaleXProperty, 0.3, 2.8, 0, 950, ease);
        Animate(ringScale, ScaleTransform.ScaleYProperty, 0.3, 2.8, 0, 950, ease);
        Animate(MediumRing, UIElement.OpacityProperty, 0.9, 0, 0, 950, ease);

        MediumCard.Visibility = Visibility.Visible;
        Canvas.SetLeft(MediumCard, left);
        Canvas.SetTop(MediumCard, top);
        var cardGroup = new TransformGroup();
        var cardScale = new ScaleTransform(0.82, 0.82);
        var cardTranslate = new TranslateTransform(0, 20);
        cardGroup.Children.Add(cardScale);
        cardGroup.Children.Add(cardTranslate);
        MediumCard.RenderTransform = cardGroup;
        Animate(MediumCard, UIElement.OpacityProperty, 0, 1, 60, 300, ease);
        Animate(MediumCard, UIElement.OpacityProperty, 1, 0, 1480, 320, ease);
        Animate(cardScale, ScaleTransform.ScaleXProperty, 0.82, 1, 60, 420, back);
        Animate(cardScale, ScaleTransform.ScaleYProperty, 0.82, 1, 60, 420, back);
        Animate(cardTranslate, TranslateTransform.YProperty, 20, 0, 60, 480, ease);
        Animate(cardTranslate, TranslateTransform.YProperty, 0, -22, 1480, 320, ease);
        Animate(MediumSweep, FrameworkElement.WidthProperty, 0, 232, 260, 700, ease);

        Spawn(center, 86, radial: true, gravity: 320);
        StartRendering();

        await Task.Delay(1850);
        MediumGlow.Visibility = Visibility.Collapsed;
        MediumRing.Visibility = Visibility.Collapsed;
        MediumCard.Visibility = Visibility.Collapsed;
    }

    // 重型：全屏暗化 + 光环扩散 + 粒子爆发 + 祝贺文案
    private async Task PlayHeavyAsync(string title)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };

        Animate(DimLayer, UIElement.OpacityProperty, 0, 0.74, 0, 340, ease);
        AnimateRing(Ring1, 0, ease);
        AnimateRing(Ring2, 140, ease);
        AnimateRing(Ring3, 280, ease);

        HeadlineText.Text = "任务完成";
        TaskTitleText.Text = title.Length > 42 ? title[..42] + "…" : title;
        EncouragementText.Text = Cheers[_random.Next(Cheers.Length)];

        Animate(MessagePanel, UIElement.OpacityProperty, 0, 1, 190, 320, ease);
        var messageScale = new ScaleTransform(0.75, 0.75);
        MessagePanel.RenderTransform = messageScale;
        Animate(messageScale, ScaleTransform.ScaleXProperty, 0.75, 1, 190, 460, back);
        Animate(messageScale, ScaleTransform.ScaleYProperty, 0.75, 1, 190, 460, back);

        Spawn(center, 140, radial: true, gravity: 240);
        StartRendering();

        await Task.Delay(2150);
        Animate(Root, UIElement.OpacityProperty, 1, 0, 0, 420, ease);
        await Task.Delay(440);
    }

    private void AnimateRing(Ellipse ring, int delayMs, IEasingFunction ease)
    {
        var scale = new ScaleTransform(0.2, 0.2);
        ring.RenderTransform = scale;
        Animate(scale, ScaleTransform.ScaleXProperty, 0.2, 3.4, delayMs, 1150, ease);
        Animate(scale, ScaleTransform.ScaleYProperty, 0.2, 3.4, delayMs, 1150, ease);
        Animate(ring, UIElement.OpacityProperty, 0.85, 0, delayMs, 1150, ease);
    }

    private void Spawn(Point origin, int count, bool radial, double gravity)
    {
        for (int i = 0; i < count; i++)
        {
            double size = 5 + _random.NextDouble() * 8;
            Shape shape = i % 3 == 0
                ? new Rectangle { Width = size * 1.7, Height = size, RadiusX = 1.5, RadiusY = 1.5 }
                : new Ellipse { Width = size, Height = size };

            shape.Fill = new SolidColorBrush(ConfettiColors[_random.Next(ConfettiColors.Length)]);
            shape.RenderTransformOrigin = new Point(0.5, 0.5);
            var rotate = new RotateTransform(_random.Next(0, 360));
            shape.RenderTransform = rotate;

            Canvas.SetLeft(shape, origin.X - size / 2);
            Canvas.SetTop(shape, origin.Y - size / 2);
            ParticleCanvas.Children.Add(shape);

            double angle = radial
                ? _random.NextDouble() * Math.PI * 2
                : -Math.PI / 2 + (_random.NextDouble() - 0.5) * 2.4;
            double speed = radial ? 170 + _random.NextDouble() * 400 : 150 + _random.NextDouble() * 300;
            double life = radial ? 1.4 + _random.NextDouble() * 0.9 : 0.9 + _random.NextDouble() * 0.7;

            _particles.Add(new Particle
            {
                Shape = shape,
                Velocity = new Vector(Math.Cos(angle) * speed, Math.Sin(angle) * speed),
                Gravity = gravity,
                Life = life,
                MaxLife = life,
                Rotate = rotate,
                Spin = (_random.NextDouble() - 0.5) * 900
            });
        }
    }

    private void StartRendering()
    {
        if (_rendering) return;
        _rendering = true;
        _lastFrame = DateTime.UtcNow;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_rendering) return;
        CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        double dt = Math.Min((now - _lastFrame).TotalSeconds, 0.05);
        _lastFrame = now;

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var particle = _particles[i];
            particle.Life -= dt;
            if (particle.Life <= 0)
            {
                ParticleCanvas.Children.Remove(particle.Shape);
                _particles.RemoveAt(i);
                continue;
            }

            particle.Velocity = new Vector(
                particle.Velocity.X,
                particle.Velocity.Y + particle.Gravity * dt);
            Canvas.SetLeft(particle.Shape, Canvas.GetLeft(particle.Shape) + particle.Velocity.X * dt);
            Canvas.SetTop(particle.Shape, Canvas.GetTop(particle.Shape) + particle.Velocity.Y * dt);
            particle.Shape.Opacity = Math.Clamp(particle.Life / particle.MaxLife, 0, 1);
            particle.Rotate.Angle += particle.Spin * dt;
        }
    }

    private void Animate(DependencyObject target, DependencyProperty property,
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
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
        _animated.Add((target, property));
    }

    private void Reset()
    {
        foreach (var (target, property) in _animated)
        {
            if (target is UIElement element) element.BeginAnimation(property, null);
            else if (target is Animatable animatable) animatable.BeginAnimation(property, null);
        }
        _animated.Clear();

        foreach (var particle in _particles)
            ParticleCanvas.Children.Remove(particle.Shape);
        _particles.Clear();

        Root.Opacity = 1;
        DimLayer.Opacity = 0;
        MessagePanel.Opacity = 0;
        FloatingLabel.Visibility = Visibility.Collapsed;
        FloatingLabel.Opacity = 1;
        if (FloatingLabel.RenderTransform is TranslateTransform translate) translate.Y = 0;

        MediumGlow.Visibility = Visibility.Collapsed;
        MediumGlow.Opacity = 0;
        MediumRing.Visibility = Visibility.Collapsed;
        MediumRing.Opacity = 0;
        MediumCard.Visibility = Visibility.Collapsed;
        MediumCard.Opacity = 0;
        MediumSweep.Width = 0;
        if (MessagePanel.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = 0.75;
            scale.ScaleY = 0.75;
        }
        foreach (var ring in new[] { Ring1, Ring2, Ring3 })
            if (ring.RenderTransform is ScaleTransform ringScale)
            {
                ringScale.ScaleX = 0.2;
                ringScale.ScaleY = 0.2;
            }
    }
}
