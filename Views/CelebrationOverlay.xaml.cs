using System.Threading;
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
    private bool _cleanedUp;
    private CancellationTokenSource? _playbackCancellation;
    private DateTime _lastFrame;

    public CelebrationOverlay() => InitializeComponent();

    public async Task PlayAsync(TaskWeight weight, string title, Point origin,
        CelebrationEffect effect = CelebrationEffect.Original)
    {
        if (_busy || _cleanedUp) return;
        _busy = true;
        using var cancellation = new CancellationTokenSource();
        _playbackCancellation = cancellation;
        var token = cancellation.Token;
        try
        {
            Visibility = Visibility.Visible;
            await Task.Delay(20, token);
            token.ThrowIfCancellationRequested();
            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            if (effect == CelebrationEffect.Applause)
            {
                await PlayApplauseAsync(weight, title, origin, token);
                return;
            }

            switch (weight)
            {
                case TaskWeight.Light:
                    await PlayLightAsync(origin, token);
                    break;
                case TaskWeight.Medium:
                    await PlayMediumAsync(title, origin, token);
                    break;
                default:
                    await PlayHeavyAsync(title, token);
                    break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            StopRendering();
            Reset();
            Visibility = Visibility.Collapsed;
            _playbackCancellation = null;
            _busy = false;
        }
    }

    public void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        _playbackCancellation?.Cancel();
        StopRendering();
        Reset();
        Visibility = Visibility.Collapsed;
    }

    // 轻型：纸屑散落 + 浮字，界面不被遮挡（原中型效果）
    private async Task PlayLightAsync(Point origin, CancellationToken token)
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

        await Task.Delay(1250, token);
        token.ThrowIfCancellationRequested();
        FloatingLabel.Visibility = Visibility.Collapsed;
    }

    // 中型：局部光晕 + 光环脉冲 + 成就卡片 + 更多彩带，仍不遮挡界面
    private async Task PlayMediumAsync(string title, Point origin, CancellationToken token)
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

        await Task.Delay(1850, token);
        token.ThrowIfCancellationRequested();
        MediumGlow.Visibility = Visibility.Collapsed;
        MediumRing.Visibility = Visibility.Collapsed;
        MediumCard.Visibility = Visibility.Collapsed;
    }

    // 重型：全屏暗化 + 光环扩散 + 粒子爆发 + 祝贺文案
    private async Task PlayHeavyAsync(string title, CancellationToken token)
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

        await Task.Delay(2150, token);
        token.ThrowIfCancellationRequested();
        Animate(Root, UIElement.OpacityProperty, 1, 0, 0, 420, ease);
        await Task.Delay(440, token);
    }

    private async Task PlayApplauseAsync(TaskWeight weight, string title, Point origin, CancellationToken token)
    {
        int intensity = weight switch { TaskWeight.Light => 0, TaskWeight.Medium => 1, _ => 2 };
        int claps = intensity switch { 0 => 2, 1 => 3, _ => 5 };
        int cycleMs = intensity switch { 0 => 380, 1 => 370, _ => 340 };
        int totalMs = 220 + claps * cycleMs + 420;
        double width = intensity switch { 0 => 244, 1 => 292, _ => 344 };
        double height = intensity switch { 0 => 230, 1 => 274, _ => 328 };
        width = Math.Min(width, Math.Max(120, ActualWidth - 24));
        double left = intensity == 2
            ? (ActualWidth - width) / 2
            : Math.Clamp(origin.X - width * 0.35, 12, Math.Max(12, ActualWidth - width - 12));
        double top = intensity == 2
            ? Math.Max(12, (ActualHeight - height) / 2)
            : Math.Clamp(origin.Y - height * 0.55, 12, Math.Max(12, ActualHeight - height - 12));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };

        ApplauseCard.Width = width;
        ApplauseCard.Height = height;
        ApplauseHands.Height = intensity switch { 0 => 106, 1 => 142, _ => 184 };
        ApplauseHeadline.FontSize = intensity switch { 0 => 17, 1 => 20, _ => 26 };
        ApplauseBadge.Text = intensity switch
        {
            0 => "完成 · 轻型任务",
            1 => "完成 · 中型任务",
            _ => "完成 · 重型任务"
        };
        ApplauseHeadline.Text = intensity switch
        {
            0 => "做得好，为你鼓掌",
            1 => "这一程，很扎实",
            _ => "为你热烈鼓掌"
        };
        ApplauseHint.Text = intensity switch
        {
            0 => "这一小步，也值得肯定",
            1 => "认真推进的每一步，都算数",
            _ => "漂亮收官，掌声属于努力的你"
        };
        ApplauseTitle.Text = title.Length > 38 ? title[..38] + "…" : title;
        ApplauseTitle.MaxWidth = width - 36;
        Canvas.SetLeft(ApplauseCard, left);
        Canvas.SetTop(ApplauseCard, top);
        ApplauseCard.Visibility = Visibility.Visible;

        var cardScale = new ScaleTransform(0.84, 0.84);
        var cardTravel = new TranslateTransform(0, 16);
        var cardTransform = new TransformGroup();
        cardTransform.Children.Add(cardScale);
        cardTransform.Children.Add(cardTravel);
        ApplauseCard.RenderTransform = cardTransform;
        Animate(cardScale, ScaleTransform.ScaleXProperty, 0.84, 1, 0, 360, back);
        Animate(cardScale, ScaleTransform.ScaleYProperty, 0.84, 1, 0, 360, back);
        AnimateSequence(ApplauseCard, UIElement.OpacityProperty, 0, totalMs, 1,
            (0, 0), (0.12, 1), ((totalMs - 300.0) / totalMs, 1), (1, 0));
        AnimateSequence(cardTravel, TranslateTransform.YProperty, 0, totalMs, 1,
            (0, 16), (0.18, 0), ((totalMs - 300.0) / totalMs, 0), (1, -12));

        var center = new Point(left + width / 2, top + 39 + ApplauseHands.Height * 0.2);
        ApplauseGlow.Visibility = Visibility.Visible;
        Canvas.SetLeft(ApplauseGlow, center.X - 140);
        Canvas.SetTop(ApplauseGlow, center.Y - 140);
        var glowScale = new ScaleTransform(0.65, 0.65);
        ApplauseGlow.RenderTransform = glowScale;
        Animate(glowScale, ScaleTransform.ScaleXProperty, 0.65, 1.35 + intensity * 0.35, 0, totalMs, ease);
        Animate(glowScale, ScaleTransform.ScaleYProperty, 0.65, 1.35 + intensity * 0.35, 0, totalMs, ease);
        AnimateSequence(ApplauseGlow, UIElement.OpacityProperty, 0, totalMs, 1,
            (0, 0), (0.18, 0.6 + intensity * 0.15), (0.65, 0.5), (1, 0));
        if (intensity == 2)
            AnimateSequence(DimLayer, UIElement.OpacityProperty, 0, totalMs, 1,
                (0, 0), (0.15, 0.38), ((totalMs - 300.0) / totalMs, 0.38), (1, 0));

        AnimateClapping(LeftHandRotation, RotateTransform.AngleProperty, 8, 24, cycleMs, claps);
        AnimateClapping(RightHandRotation, RotateTransform.AngleProperty, -8, -24, cycleMs, claps);
        AnimateClapping(LeftHandTravel, TranslateTransform.XProperty, -16, -2, cycleMs, claps);
        AnimateClapping(RightHandTravel, TranslateTransform.XProperty, 16, 2, cycleMs, claps);
        AnimateSequence(ApplauseSpark, UIElement.OpacityProperty, 220, cycleMs, claps,
            (0, 0), (0.34, 0), (0.39, 1), (0.7, 0), (1, 0));
        AnimateApplauseWave(ApplauseWave1, center, 220, cycleMs, claps, 2.8 + intensity * 0.8);
        if (intensity >= 1)
            AnimateApplauseWave(ApplauseWave2, center, 290, cycleMs, claps, 3.5 + intensity * 0.8);
        if (intensity == 2)
            AnimateApplauseWave(ApplauseWave3, center, 360, cycleMs, claps, 5.1);

        StartRendering();
        int impactMs = 220 + (int)(cycleMs * 0.38);
        await Task.Delay(impactMs, token);
        token.ThrowIfCancellationRequested();
        int confettiCount = intensity switch { 0 => 12, 1 => 20, _ => 30 };
        Spawn(center, confettiCount, radial: true, gravity: 360);
        for (int clap = 1; clap < claps; clap++)
        {
            await Task.Delay(cycleMs, token);
            token.ThrowIfCancellationRequested();
            Spawn(center, confettiCount, radial: true, gravity: 360);
        }
        await Task.Delay(totalMs - impactMs - (claps - 1) * cycleMs, token);
    }

    private void AnimateClapping(DependencyObject target, DependencyProperty property,
        double open, double closed, int cycleMs, int claps)
    {
        AnimateSequence(target, property, 220, cycleMs, claps,
            (0, open), (0.38, closed), (0.48, closed), (0.9, open), (1, open));
    }

    private void AnimateApplauseWave(Ellipse wave, Point center, int delayMs,
        int cycleMs, int claps, double expansion)
    {
        wave.Visibility = Visibility.Visible;
        Canvas.SetLeft(wave, center.X - 50);
        Canvas.SetTop(wave, center.Y - 50);
        var scale = new ScaleTransform(0.25, 0.25);
        wave.RenderTransform = scale;
        AnimateSequence(scale, ScaleTransform.ScaleXProperty, delayMs, cycleMs, claps,
            (0, 0.25), (0.38, 0.25), (1, expansion));
        AnimateSequence(scale, ScaleTransform.ScaleYProperty, delayMs, cycleMs, claps,
            (0, 0.25), (0.38, 0.25), (1, expansion));
        AnimateSequence(wave, UIElement.OpacityProperty, delayMs, cycleMs, claps,
            (0, 0), (0.37, 0), (0.4, 0.75), (1, 0));
    }

    private void AnimateSequence(DependencyObject target, DependencyProperty property,
        int delayMs, int durationMs, int repetitions, params (double Progress, double Value)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = TimeSpan.FromMilliseconds(durationMs),
            RepeatBehavior = new RepeatBehavior(repetitions)
        };
        foreach (var (progress, value) in frames)
        {
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(value,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * progress)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
        }
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
        _animated.Add((target, property));
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

        ApplauseCard.Visibility = Visibility.Collapsed;
        ApplauseCard.Opacity = 0;
        ApplauseCard.RenderTransform = Transform.Identity;
        ApplauseCard.Width = 292;
        ApplauseCard.Height = 274;
        ApplauseHands.Height = 142;
        ApplauseHeadline.FontSize = 20;
        ApplauseTitle.Text = string.Empty;
        ApplauseGlow.Visibility = Visibility.Collapsed;
        ApplauseGlow.Opacity = 0;
        ApplauseGlow.RenderTransform = new ScaleTransform(0.65, 0.65);
        ApplauseSpark.Opacity = 0;
        LeftHandRotation.Angle = 8;
        RightHandRotation.Angle = -8;
        LeftHandTravel.X = -16;
        RightHandTravel.X = 16;
        foreach (var wave in new[] { ApplauseWave1, ApplauseWave2, ApplauseWave3 })
        {
            wave.Visibility = Visibility.Collapsed;
            wave.Opacity = 0;
            wave.RenderTransform = new ScaleTransform(0.25, 0.25);
        }

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
