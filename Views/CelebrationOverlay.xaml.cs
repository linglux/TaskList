using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TaskList.Models;
using TaskList.Services;

namespace TaskList.Views;

public partial class CelebrationOverlay : UserControl
{
    private sealed class FireworkSpark
    {
        public required Ellipse Head { get; init; }
        public required Polyline Trail { get; init; }
        public Point Position { get; set; }
        public Vector Velocity { get; set; }
        public double Life { get; set; }
        public double MaxLife { get; init; }
        public Queue<Point> History { get; } = new();
    }

    private readonly List<FireworkSpark> _fireworkSparks = new();

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

    private sealed class ClapContour
    {
        private readonly List<Point> _openPoints = new List<Point>();
        private readonly List<Point> _contactPoints = new List<Point>();
        private readonly List<Point> _impactPoints = new List<Point>();
        private readonly List<Action<Point>> _setters = new List<Action<Point>>();

        public PathGeometry Geometry { get; private set; }

        public ClapContour(Geometry contactGeometry, bool front)
        {
            Geometry = PathGeometry.CreateFromGeometry(contactGeometry).Clone();
            foreach (var figure in Geometry.Figures)
            {
                AddPoint(figure.StartPoint, point => figure.StartPoint = point, front);
                foreach (var segment in figure.Segments)
                {
                    var bezier = segment as BezierSegment;
                    if (bezier != null)
                    {
                        AddPoint(bezier.Point1, point => bezier.Point1 = point, front);
                        AddPoint(bezier.Point2, point => bezier.Point2 = point, front);
                        AddPoint(bezier.Point3, point => bezier.Point3 = point, front);
                        continue;
                    }

                    var polyBezier = segment as PolyBezierSegment;
                    if (polyBezier != null)
                    {
                        for (int i = 0; i < polyBezier.Points.Count; i++)
                        {
                            int index = i;
                            AddPoint(polyBezier.Points[index], point => polyBezier.Points[index] = point, front);
                        }
                        continue;
                    }

                    throw new InvalidOperationException(LocalizationService.T("鼓掌轮廓必须使用三次贝塞尔线段。"));
                }
            }
            SetPose(1, 0);
        }

        public void SetPose(double closure, double impact)
        {
            for (int i = 0; i < _setters.Count; i++)
            {
                var point = _openPoints[i] + (_contactPoints[i] - _openPoints[i]) * closure
                    + (_impactPoints[i] - _contactPoints[i]) * impact;
                _setters[i](point);
            }
        }

        private void AddPoint(Point contact, Action<Point> setter, bool front)
        {
            var offset = new Vector(front ? 28 : 0, 0);
            _openPoints.Add(ToStage(CreatePosePoint(contact, front, false)) + offset);
            _contactPoints.Add(ToStage(contact) + offset);
            _impactPoints.Add(ToStage(CreatePosePoint(contact, front, true)) + offset);
            _setters.Add(setter);
        }

        private static Point CreatePosePoint(Point point, bool front, bool impact)
        {
            double progress = Math.Clamp((point.Y - 138) / 52, 0, 1);
            double influence = 1 - progress * progress * (3 - 2 * progress);
            var wrist = front ? new Point(151, 142) : new Point(121, 142);
            double angle = (impact ? (front ? 0.8 : -0.8) : (front ? -11 : 11)) * Math.PI / 180;
            double x = point.X - wrist.X;
            double y = point.Y - wrist.Y;
            double shift = impact ? (front ? 1.2 : -1.2) : (front ? -17 : 17);
            var posed = new Point(wrist.X + x * Math.Cos(angle) - y * Math.Sin(angle) + shift,
                wrist.Y + x * Math.Sin(angle) + y * Math.Cos(angle) + (impact ? 1.5 : 0));
            return point + (posed - point) * influence;
        }

        private static Point ToStage(Point point) => point;
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
    private readonly Stopwatch _clapClock = new();
    private readonly List<ClapContour> _clapContours = new();
    private const int ClapLeadInMs = 220;
    private const double ClapImpactPhase = 0.43;
    private static readonly Point ClapCenter = new(133, 87);
    private static readonly Point SparkCenter = new(136, 25);
    private int _clapCycleMs;
    private int _clapCount;
    private int _clapIntensity;
    private int _nextClapImpact;
    private bool _clapping;
    private bool _busy;
    private bool _rendering;
    private bool _cleanedUp;
    private CancellationTokenSource? _playbackCancellation;
    private DateTime _lastFrame;
    private string _mediumHintKey = MediumHints[0];
    private string _cheerKey = Cheers[0];
    private int _applauseTextIntensity = 1;
    private Viewbox? _activeStamp;
    private int _stampIntensity;

    public CelebrationOverlay()
    {
        InitializeComponent();
        LocalizationService.Instance.PropertyChanged += OnLanguageChanged;
        RefreshCelebrationText();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshCelebrationText();
        if (_activeStamp is not null)
            _activeStamp.Child = CreateSealImpression(_stampIntensity);
    }

    private void RefreshCelebrationText()
    {
        HeadlineText.Text = LocalizationService.T("任务完成");
        MediumHint.Text = LocalizationService.T(_mediumHintKey);
        EncouragementText.Text = LocalizationService.T(_cheerKey);
        ApplauseBadge.Text = LocalizationService.T(_applauseTextIntensity switch
        {
            0 => "完成 · 轻型任务",
            1 => "完成 · 中型任务",
            _ => "完成 · 重型任务"
        });
        ApplauseHeadline.Text = LocalizationService.T(_applauseTextIntensity switch
        {
            0 => "做得好，为你鼓掌",
            1 => "这一程，很扎实",
            _ => "为你热烈鼓掌"
        });
        ApplauseHint.Text = LocalizationService.T(_applauseTextIntensity switch
        {
            0 => "这一小步，也值得肯定",
            1 => "认真推进的每一步，都算数",
            _ => "漂亮收官，掌声属于努力的你"
        });
    }

    private void EnsureClapContours()
    {
        if (_clapContours.Count > 0) return;
        foreach (var (path, front) in new[]
        {
            (BackHandOutline, false), (BackHandShading, false), (BackHandDetails, false),
            (FrontHandOutline, true), (FrontHandShading, true), (FrontHandDetails, true)
        })
        {
            var contour = new ClapContour(path.Data, front);
            path.Data = contour.Geometry;
            _clapContours.Add(contour);
        }
    }

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

            switch (effect)
            {
                case CelebrationEffect.Applause:
                    await PlayApplauseAsync(weight, title, origin, token);
                    return;
                case CelebrationEffect.MinimalCheck:
                    await PlayMinimalCheckAsync(weight, origin, token);
                    return;
                case CelebrationEffect.CompletionStamp:
                    await PlayCompletionStampAsync(weight, origin, token);
                    return;
                case CelebrationEffect.Starlight:
                    await PlayStarlightAsync(weight, origin, token);
                    return;
                case CelebrationEffect.Fireworks:
                    await PlayFireworksAsync(weight, origin, token);
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
        LocalizationService.Instance.PropertyChanged -= OnLanguageChanged;
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
        _mediumHintKey = MediumHints[_random.Next(MediumHints.Length)];
        RefreshCelebrationText();

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

        TaskTitleText.Text = title.Length > 42 ? title[..42] + "…" : title;
        _cheerKey = Cheers[_random.Next(Cheers.Length)];
        RefreshCelebrationText();

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
        int cycleMs = intensity switch { 0 => 520, 1 => 490, _ => 460 };
        int totalMs = ClapLeadInMs + claps * cycleMs + 420;
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
        _applauseTextIntensity = intensity;
        RefreshCelebrationText();
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

        ApplauseCard.UpdateLayout();
        var center = ApplauseStage.TranslatePoint(ClapCenter, ParticleCanvas);
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

        InitializeClapping(cycleMs, claps, intensity);
        StartRendering();
        await Task.Delay(totalMs, token);
    }

    private async Task PlayMinimalCheckAsync(TaskWeight weight, Point origin, CancellationToken token)
    {
        int intensity = EffectIntensity(weight);
        int totalMs = intensity switch { 0 => 520, 1 => 820, _ => 1150 };
        double size = 48 + intensity * 16;
        var center = FitEffectCenter(origin, size);
        var color = Color.FromRgb(0x05, 0x96, 0x69);
        var host = AddEffect(new Canvas { Width = size, Height = size }, center);
        var geometry = new PathGeometry();
        var start = new Point(size * 0.18, size * 0.52);
        var middle = new Point(size * 0.42, size * 0.75);
        var end = new Point(size * 0.84, size * 0.27);
        var figure = new PathFigure { StartPoint = start };
        figure.Segments.Add(new LineSegment(middle, true));
        figure.Segments.Add(new LineSegment(end, true));
        geometry.Figures.Add(figure);
        double thickness = size * 0.075;
        double dash = ((middle - start).Length + (end - middle).Length) / thickness;
        var check = new Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeDashArray = new DoubleCollection { dash, dash },
            StrokeDashOffset = dash
        };
        host.Children.Add(check);
        Animate(check, Shape.StrokeDashOffsetProperty, dash, 0, 30, 240,
            new CubicEase { EasingMode = EasingMode.EaseOut });
        var scale = new ScaleTransform(0.85, 0.85);
        host.RenderTransform = scale;
        AnimateSequence(scale, ScaleTransform.ScaleXProperty, 0, 360, 1,
            (0, 0.85), (0.7, 1.08), (1, 1));
        AnimateSequence(scale, ScaleTransform.ScaleYProperty, 0, 360, 1,
            (0, 0.85), (0.7, 1.08), (1, 1));
        FadeEffect(host, 0, totalMs);
        if (intensity > 0) AddEffectRing(center, size * 0.9, color, 90, totalMs - 90);
        if (intensity == 2) AddEffectHalo(center, size * 2.2, color, 0, totalMs);
        await Task.Delay(totalMs, token);
    }

    private async Task PlayCompletionStampAsync(TaskWeight weight, Point origin, CancellationToken token)
    {
        int intensity = EffectIntensity(weight);
        int totalMs = 1450 + intensity * 300;
        double size = 166 + intensity * 34;
        var center = FitEffectCenter(origin, size * 0.7 + 24);
        var stamp = AddEffect(new Viewbox
        {
            Width = size,
            Height = size,
            Child = CreateSealImpression(intensity)
        }, center);
        _activeStamp = stamp;
        _stampIntensity = intensity;
        var scale = new ScaleTransform(1.55, 1.55);
        var rotation = new RotateTransform(-14);
        var travel = new TranslateTransform(0, -36);
        var transforms = new TransformGroup();
        transforms.Children.Add(scale);
        transforms.Children.Add(rotation);
        transforms.Children.Add(travel);
        stamp.RenderTransform = transforms;
        AnimateSequence(scale, ScaleTransform.ScaleXProperty, 0, 440, 1,
            (0, 1.55), (0.42, 0.94), (0.7, 1.04), (1, 1));
        AnimateSequence(scale, ScaleTransform.ScaleYProperty, 0, 440, 1,
            (0, 1.55), (0.42, 0.94), (0.7, 1.04), (1, 1));
        Animate(travel, TranslateTransform.YProperty, -36, 0, 0, 185,
            new CubicEase { EasingMode = EasingMode.EaseIn });
        Animate(rotation, RotateTransform.AngleProperty, -14, -7, 0, 360);
        RevealEffect(stamp, 0, totalMs);
        for (int i = 0; i < 12 + intensity * 6; i++)
        {
            double angle = i * Math.PI * 2 / (12 + intensity * 6);
            double distance = size * (0.51 + _random.NextDouble() * 0.05);
            double speckSize = 1.5 + _random.NextDouble() * 2;
            var speck = AddEffect(new Ellipse
            {
                Width = speckSize,
                Height = speckSize,
                Fill = new SolidColorBrush(Color.FromArgb(140, 0x85, 0x1C, 0x22))
            }, center + new Vector(Math.Cos(angle), Math.Sin(angle)) * distance);
            var movement = new TranslateTransform();
            speck.RenderTransform = movement;
            Animate(movement, TranslateTransform.XProperty, 0, Math.Cos(angle) * 18, 170, 380);
            Animate(movement, TranslateTransform.YProperty, 0, Math.Sin(angle) * 18, 170, 380);
            FadeEffect(speck, 170, 380);
        }
        await Task.Delay(totalMs, token);
    }

    private Canvas CreateSealImpression(int intensity)
    {
        var random = new Random(1979 + intensity);
        var inkGeometry = new GeometryGroup();
        foreach (var (radius, width) in new[] { (88.0, 5.2), (79.0, 1.7) })
        {
            var ring = new StreamGeometry();
            using (var context = ring.Open())
            {
                for (int i = 0; i < 160; i++)
                {
                    double angle = i * Math.PI * 2 / 160;
                    double unevenRadius = radius + Math.Sin(angle * 7) * 0.8 + random.NextDouble() * 1.4;
                    var point = new Point(100 + Math.Cos(angle) * unevenRadius, 100 + Math.Sin(angle) * unevenRadius);
                    if (i == 0) context.BeginFigure(point, false, true);
                    else context.LineTo(point, true, false);
                }
            }
            inkGeometry.Children.Add(ring.GetWidenedPathGeometry(new Pen(Brushes.Black, width)
            {
                LineJoin = PenLineJoin.Round
            }));
        }
        if (LocalizationService.IsEnglish)
        {
            var text = new FormattedText("DONE", LocalizationService.Culture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                46, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var glyph = text.BuildGeometry(new Point());
            var bounds = glyph.Bounds;
            double scale = Math.Min(140 / bounds.Width, 90 / bounds.Height);
            glyph.Transform = new MatrixTransform(scale, 0, 0, scale,
                100 - (bounds.X + bounds.Width / 2) * scale,
                100 - (bounds.Y + bounds.Height / 2) * scale);
            inkGeometry.Children.Add(glyph);
        }
        else
        {
            var glyphs = new[]
            {
                ("M 4,6 L 36,6 L 36,22 L 10,22 M 10,10 L 10,42 Q 10,48 17,48 L 33,48 Q 39,48 39,41 L 39,35", 1.08, 1.12, 113.0, 35.0),
                ("M 20,2 L 20,7 M 4,17 L 4,9 L 38,9 L 38,17 M 9,22 L 33,22 M 5,31 L 37,31 M 15,31 L 15,39 Q 15,45 6,48 M 27,31 L 27,42 Q 27,48 33,48 L 38,48 L 38,42", 1.22, 1.18, 104.0, 104.0),
                ("M 6,15 L 34,15 M 9,15 L 9,34 Q 9,44 3,48 M 9,26 L 21,26 L 21,39 Q 21,44 16,44 M 26,3 Q 25,30 35,44 Q 39,50 40,41 M 36,24 Q 30,37 24,45 M 34,5 L 38,9", 1.35, 2.22, 40.0, 46.0)
            };
            foreach (var (data, scaleX, scaleY, x, y) in glyphs)
            {
                var glyph = Geometry.Parse(data).GetWidenedPathGeometry(new Pen(Brushes.Black, 3.6)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                });
                glyph.Transform = new MatrixTransform(scaleX, 0, 0, scaleY, x, y);
                inkGeometry.Children.Add(glyph);
            }
        }
        inkGeometry.Freeze();
        var wear = new DrawingGroup();
        var cutouts = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (int i = 0; i < 210; i++)
        {
            double angle = random.NextDouble() * Math.PI * 2;
            double radius = Math.Sqrt(random.NextDouble()) * 90;
            var hole = new EllipseGeometry(new Point(100 + Math.Cos(angle) * radius, 100 + Math.Sin(angle) * radius),
                0.35 + random.NextDouble() * 1.5, 0.2 + random.NextDouble() * 0.65);
            cutouts.Children.Add(hole);
            wear.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb((byte)random.Next(25, 120), 255, 255, 255)), null, hole));
        }
        for (int i = 0; i < 22; i++)
        {
            double angle = random.NextDouble() * Math.PI * 2;
            cutouts.Children.Add(new EllipseGeometry(new Point(100 + Math.Cos(angle) * 89, 100 + Math.Sin(angle) * 89),
                1.2 + random.NextDouble() * 2.8, 0.8 + random.NextDouble() * 2));
        }
        wear.Children.Insert(0, new GeometryDrawing(Brushes.White, null, new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, 200, 200)), cutouts)));
        wear.Freeze();
        var mask = new DrawingBrush(wear);
        mask.Freeze();
        var ink = new RadialGradientBrush();
        ink.GradientStops.Add(new GradientStop(Color.FromRgb(0x6F, 0x15, 0x1D), 0));
        ink.GradientStops.Add(new GradientStop(Color.FromRgb(0x9B, 0x28, 0x2D), 0.58));
        ink.GradientStops.Add(new GradientStop(Color.FromRgb(0x79, 0x19, 0x21), 1));
        ink.Freeze();
        var impression = new Canvas { Width = 200, Height = 200 };
        impression.Children.Add(new Path { Width = 200, Height = 200, Data = inkGeometry, Fill = ink, OpacityMask = mask });
        return impression;
    }

    private async Task PlayStarlightAsync(TaskWeight weight, Point origin, CancellationToken token)
    {
        int intensity = EffectIntensity(weight);
        int count = 6 + intensity * 3;
        int stepMs = 75 - intensity * 5;
        int mainStarMs = 100 + (count - 1) * stepMs + 360;
        int totalMs = mainStarMs + 1000 + intensity * 100;
        double radius = 112 + intensity * 32;
        var center = FitEffectCenter(origin, radius * 1.4 + 20);
        var gold = Color.FromRgb(0xFB, 0xBF, 0x24);
        var sky = Color.FromRgb(0x7D, 0xD3, 0xFC);
        var night = new RadialGradientBrush();
        night.GradientStops.Add(new GradientStop(Color.FromArgb(220, 0x1B, 0x27, 0x4D), 0));
        night.GradientStops.Add(new GradientStop(Color.FromArgb(175, 0x25, 0x37, 0x61), 0.55));
        night.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x25, 0x37, 0x61), 1));
        var backdrop = AddEffect(new Ellipse { Width = radius * 2.8, Height = radius * 2.8, Fill = night }, center);
        Panel.SetZIndex(backdrop, -2);
        RevealEffect(backdrop, 0, totalMs);
        var layout = new[]
        {
            new Vector(-0.92, 0.28), new Vector(-0.96, -0.20), new Vector(-0.70, -0.63),
            new Vector(-0.28, -0.89), new Vector(0.18, -0.84), new Vector(0.62, -0.69),
            new Vector(0.91, -0.32), new Vector(0.95, 0.14), new Vector(0.74, 0.56),
            new Vector(0.38, 0.85), new Vector(-0.10, 0.94), new Vector(-0.52, 0.73)
        };
        var points = Enumerable.Range(0, count).Select(i => center + layout[i * layout.Length / count] * radius).ToArray();
        for (int i = 0; i < count; i++)
        {
            int delay = 100 + i * stepMs;
            if (i > 0) DrawConstellationLink(points[i - 1], points[i], sky, delay, totalMs);
            AddEffectHalo(points[i], 40 + intensity * 10, sky, delay, totalMs - delay, 130);
            var node = AddEffect(CreateStar(15 + intensity * 3, Brushes.LightYellow), points[i]);
            node.Stroke = new SolidColorBrush(sky);
            node.StrokeThickness = 1;
            var nodeScale = new ScaleTransform(0.4, 0.4);
            node.RenderTransform = nodeScale;
            Animate(nodeScale, ScaleTransform.ScaleXProperty, 0.4, 1, delay, 210,
                new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 });
            Animate(nodeScale, ScaleTransform.ScaleYProperty, 0.4, 1, delay, 210,
                new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 });
            RevealEffect(node, delay, totalMs);
            var path = new PathGeometry();
            var figure = new PathFigure { StartPoint = points[i] };
            figure.Segments.Add(new QuadraticBezierSegment(
                center + (points[i] - center) * 0.3 + new Vector(12, -18), center, true));
            path.Figures.Add(figure);
            path.Freeze();
            var energy = AddEffect(new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(gold) }, new Point());
            var movement = new TranslateTransform(points[i].X, points[i].Y);
            energy.RenderTransform = movement;
            int gatherDelay = mainStarMs - 290 + i % 3 * 20;
            AnimateAlongPath(movement, path, gatherDelay, 290);
            FadeEffect(energy, gatherDelay, 290);
        }
        double starSize = 80 + intensity * 26;
        AddEffectHalo(center, starSize * 2.8, gold, mainStarMs, totalMs - mainStarMs, 175);
        var star = AddEffect(CreateStar(starSize, new LinearGradientBrush(
            Color.FromRgb(0xFF, 0xF3, 0xB0), Color.FromRgb(0xF5, 0x9E, 0x0B), 90)), center);
        star.Stroke = new SolidColorBrush(Color.FromRgb(0xF5, 0xBE, 0x47));
        star.StrokeThickness = 1.8;
        var scale = new ScaleTransform(0.35, 0.35);
        star.RenderTransform = scale;
        AnimateSequence(scale, ScaleTransform.ScaleXProperty, mainStarMs, 450, 1,
            (0, 0.35), (0.6, 1.15), (1, 1));
        AnimateSequence(scale, ScaleTransform.ScaleYProperty, mainStarMs, 450, 1,
            (0, 0.35), (0.6, 1.15), (1, 1));
        RevealEffect(star, mainStarMs, totalMs);
        AddEffectRing(center, starSize, gold, mainStarMs + 80, 650);
        for (int i = 0; i < 8 + intensity * 4; i++)
        {
            double angle = i * Math.PI * 2 / (8 + intensity * 4);
            var direction = new Vector(Math.Cos(angle), Math.Sin(angle));
            var ray = new Line
            {
                X1 = center.X + direction.X * starSize * 0.58,
                Y1 = center.Y + direction.Y * starSize * 0.58,
                X2 = center.X + direction.X * starSize * 0.85,
                Y2 = center.Y + direction.Y * starSize * 0.85,
                Stroke = new SolidColorBrush(gold),
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            EffectCanvas.Children.Add(ray);
            FadeEffect(ray, mainStarMs + 180, 500);
        }
        await Task.Delay(totalMs, token);
    }

    private void DrawConstellationLink(Point from, Point to, Color color, int delayMs, int totalMs)
    {
        double dash = (to - from).Length / 2;
        var link = new Line
        {
            X1 = from.X,
            Y1 = from.Y,
            X2 = to.X,
            Y2 = to.Y,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { dash, dash },
            StrokeDashOffset = dash
        };
        EffectCanvas.Children.Add(link);
        Animate(link, Shape.StrokeDashOffsetProperty, dash, 0, delayMs, 180);
        RevealEffect(link, delayMs, totalMs);
    }

    private async Task PlayFireworksAsync(TaskWeight weight, Point origin, CancellationToken token)
    {
        int intensity = EffectIntensity(weight);
        double radius = 132 + intensity * 38;
        double spread = 90 + intensity * 34;
        double scale = Math.Min(1, Math.Min(ActualWidth, ActualHeight) / (2 * (radius * 1.2 + spread + 32)));
        radius *= scale;
        spread *= scale;
        var center = FitEffectCenter(origin, radius * 1.2 + spread + 24);
        var layout = intensity switch
        {
            0 => new[] { new Vector(-0.5, -0.15), new Vector(0.5, 0.15) },
            1 => new[] { new Vector(-0.6, 0.1), new Vector(0.6, 0), new Vector(-0.15, -0.65), new Vector(0.15, 0.6) },
            _ => new[] { new Vector(-0.85, -0.1), new Vector(0.85, -0.15), new Vector(-0.38, -0.7),
                new Vector(0.36, -0.68), new Vector(-0.4, 0.7), new Vector(0.43, 0.67) }
        };
        StartRendering();
        for (int wave = 0; wave <= intensity; wave++)
        {
            token.ThrowIfCancellationRequested();
            for (int column = 0; column < 2; column++)
            {
                var target = center + layout[wave * 2 + column] * spread;
                var color = ConfettiColors[(wave * 2 + column + intensity) % 5];
                double rocketSize = 10 + intensity * 2;
                var rocket = AddEffect(new Canvas { Width = rocketSize, Height = 64 + intensity * 10 }, target);
                var tail = new Rectangle
                {
                    Width = 3 + intensity,
                    Height = rocket.Height - 7,
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    Fill = new LinearGradientBrush(color, Colors.Transparent, 90)
                };
                Canvas.SetLeft(tail, (rocketSize - tail.Width) / 2);
                Canvas.SetTop(tail, 6);
                rocket.Children.Add(tail);
                rocket.Children.Add(new Ellipse { Width = rocketSize, Height = rocketSize, Fill = new SolidColorBrush(color) });
                var movement = new TranslateTransform(0, 154 + intensity * 30);
                rocket.RenderTransform = movement;
                Animate(movement, TranslateTransform.YProperty, movement.Y, (rocket.Height - rocketSize) / 2, 0, 330,
                    new CubicEase { EasingMode = EasingMode.EaseOut });
                FadeEffect(rocket, 0, 350);
            }
            await Task.Delay(330, token);
            token.ThrowIfCancellationRequested();
            for (int column = 0; column < 2; column++)
            {
                var target = center + layout[wave * 2 + column] * spread;
                var color = ConfettiColors[(wave * 2 + column + intensity) % 5];
                SpawnFirework(target, radius, 32 + intensity * 14, color);
                SpawnFirework(target, radius * 0.5, 12 + intensity * 6, Color.FromRgb(0xFB, 0xBF, 0x24), true);
                AddEffectHalo(target, radius * 1.7, color, 0, 850, (byte)(110 + intensity * 25));
            }
        }
        await Task.Delay(1800, token);
    }

    private static int EffectIntensity(TaskWeight weight)
        => weight switch { TaskWeight.Light => 0, TaskWeight.Medium => 1, _ => 2 };

    private Point FitEffectCenter(Point origin, double margin)
        => new(Math.Clamp(origin.X, Math.Min(margin, ActualWidth / 2), Math.Max(ActualWidth / 2, ActualWidth - margin)),
            Math.Clamp(origin.Y, Math.Min(margin, ActualHeight / 2), Math.Max(ActualHeight / 2, ActualHeight - margin)));

    private T AddEffect<T>(T element, Point center) where T : FrameworkElement
    {
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        Canvas.SetLeft(element, center.X - element.Width / 2);
        Canvas.SetTop(element, center.Y - element.Height / 2);
        EffectCanvas.Children.Add(element);
        return element;
    }

    private void FadeEffect(UIElement element, int delayMs, int durationMs)
    {
        element.Opacity = 0;
        AnimateSequence(element, UIElement.OpacityProperty, delayMs, durationMs, 1,
            (0, 0), (0.12, 1), (0.65, 1), (1, 0));
    }

    private void RevealEffect(UIElement element, int delayMs, int endMs)
    {
        int durationMs = endMs - delayMs;
        element.Opacity = 0;
        AnimateSequence(element, UIElement.OpacityProperty, delayMs, durationMs, 1,
            (0, 0), (Math.Min(120.0 / durationMs, 0.2), 1), (Math.Max(0.3, 1 - 300.0 / durationMs), 1), (1, 0));
    }

    private void AddEffectHalo(Point center, double size, Color color, int delayMs, int durationMs, byte opacity = 70)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(opacity, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(opacity * 0.34), color.R, color.G, color.B), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        var halo = AddEffect(new Ellipse { Width = size, Height = size, Fill = brush }, center);
        Panel.SetZIndex(halo, -1);
        FadeEffect(halo, delayMs, durationMs);
    }

    private void AddEffectRing(Point center, double size, Color color, int delayMs, int durationMs)
    {
        var ring = AddEffect(new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 1.5,
            Opacity = 0
        }, center);
        var scale = new ScaleTransform(0.65, 0.65);
        ring.RenderTransform = scale;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Animate(scale, ScaleTransform.ScaleXProperty, 0.65, 1.9, delayMs, durationMs, ease);
        Animate(scale, ScaleTransform.ScaleYProperty, 0.65, 1.9, delayMs, durationMs, ease);
        FadeEffect(ring, delayMs, durationMs);
    }

    private static Path CreateStar(double size, Brush fill)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (int i = 0; i < 10; i++)
            {
                double angle = -Math.PI / 2 + i * Math.PI / 5;
                double radius = size * (i % 2 == 0 ? 0.48 : 0.22);
                var point = new Point(size / 2 + Math.Cos(angle) * radius,
                    size / 2 + Math.Sin(angle) * radius);
                if (i == 0) context.BeginFigure(point, true, true);
                else context.LineTo(point, true, false);
            }
        }
        geometry.Freeze();
        return new Path { Width = size, Height = size, Data = geometry, Fill = fill, Opacity = 0 };
    }

    private void AnimateAlongPath(TranslateTransform movement, PathGeometry path, int delayMs, int durationMs)
    {
        foreach (var (property, source) in new[]
        {
            (TranslateTransform.XProperty, PathAnimationSource.X),
            (TranslateTransform.YProperty, PathAnimationSource.Y)
        })
        {
            var animation = new DoubleAnimationUsingPath
            {
                PathGeometry = path,
                Source = source,
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                Duration = TimeSpan.FromMilliseconds(durationMs)
            };
            ApplyEffectAnimation(movement, property, animation);
        }
    }

    private void InitializeClapping(int cycleMs, int claps, int intensity)
    {
        EnsureClapContours();
        _clapCycleMs = cycleMs;
        _clapCount = claps;
        _clapIntensity = intensity;
        _nextClapImpact = 0;
        foreach (var contour in _clapContours)
            contour.SetPose(1, 0);
        ApplauseSpark.RenderTransform = new ScaleTransform(1, 1, SparkCenter.X, SparkCenter.Y);
        var waves = new[] { ApplauseWave1, ApplauseWave2, ApplauseWave3 };
        for (int i = 0; i < waves.Length; i++)
        {
            waves[i].Visibility = i <= intensity ? Visibility.Visible : Visibility.Collapsed;
            waves[i].Opacity = 0;
            waves[i].RenderTransform = new ScaleTransform(0.25, 0.25);
        }
        _clapping = true;
        _clapClock.Restart();
        UpdateClapping();
    }

    private void UpdateClapping()
    {
        double clockMs = _clapClock.Elapsed.TotalMilliseconds;
        double elapsedMs = clockMs - ClapLeadInMs;
        bool active = elapsedMs >= 0 && elapsedMs < _clapCount * _clapCycleMs;
        double phase = active ? elapsedMs % _clapCycleMs / _clapCycleMs : 0;
        double closure;
        if (elapsedMs < 0)
        {
            double progress = Math.Clamp(clockMs / ClapLeadInMs, 0, 1);
            closure = 1 - progress * progress * (3 - 2 * progress);
        }
        else if (!active)
            closure = 1;
        else if (phase < 0.12)
            closure = -0.04 * Math.Sin(phase / 0.12 * Math.PI / 2);
        else if (phase < ClapImpactPhase)
            closure = -0.04 + 1.04 * Math.Pow((phase - 0.12) / (ClapImpactPhase - 0.12), 2.2);
        else if (phase < 0.49)
            closure = 1;
        else
        {
            double recoil = Math.Pow(Math.Max(0, 1 - (phase - 0.49) / 0.41), 3);
            bool lastClap = elapsedMs >= (_clapCount - 1) * _clapCycleMs;
            double settle = Math.Clamp((phase - 0.49) / 0.51, 0, 1);
            settle = settle * settle * (3 - 2 * settle);
            closure = lastClap ? 1 - 0.3 * Math.Sin(settle * Math.PI) : recoil;
        }

        double compression = active && phase >= ClapImpactPhase && phase < 0.49
            ? Math.Sin((phase - ClapImpactPhase) / (0.49 - ClapImpactPhase) * Math.PI) : 0;
        foreach (var contour in _clapContours)
            contour.SetPose(closure, compression);

        double flash = active && phase >= ClapImpactPhase && phase < 0.71
            ? Math.Pow(1 - (phase - ClapImpactPhase) / 0.28, 2) : 0;
        ApplauseSpark.Opacity = flash;
        if (ApplauseSpark.RenderTransform is ScaleTransform sparkScale)
            sparkScale.ScaleX = sparkScale.ScaleY = 1 + flash * 0.22;

        var center = ApplauseStage.TranslatePoint(ClapCenter, ParticleCanvas);
        Canvas.SetLeft(ApplauseGlow, center.X - 140);
        Canvas.SetTop(ApplauseGlow, center.Y - 140);
        UpdateClapWave(ApplauseWave1, center, elapsedMs, 0, 2.8 + _clapIntensity * 0.8);
        UpdateClapWave(ApplauseWave2, center, elapsedMs, 0.06, 3.5 + _clapIntensity * 0.8);
        UpdateClapWave(ApplauseWave3, center, elapsedMs, 0.12, 5.1);

        while (_nextClapImpact < _clapCount &&
               elapsedMs >= (_nextClapImpact + ClapImpactPhase) * _clapCycleMs)
        {
            Spawn(center, _clapIntensity switch { 0 => 12, 1 => 20, _ => 30 }, radial: true, gravity: 360);
            _nextClapImpact++;
        }
    }

    private void UpdateClapWave(Ellipse wave, Point center, double elapsedMs, double offset, double expansion)
    {
        if (wave.Visibility != Visibility.Visible) return;
        double cycle = elapsedMs / _clapCycleMs - ClapImpactPhase - offset;
        if (cycle < 0 || cycle >= _clapCount)
        {
            wave.Opacity = 0;
            return;
        }
        double progress = cycle - Math.Floor(cycle);
        wave.Opacity = 0.75 * Math.Pow(1 - progress, 2);
        Canvas.SetLeft(wave, center.X - 50);
        Canvas.SetTop(wave, center.Y - 50);
        if (wave.RenderTransform is ScaleTransform scale)
            scale.ScaleX = scale.ScaleY = 0.25 + expansion * (1 - Math.Pow(1 - progress, 2));
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
        ApplyEffectAnimation(target, property, animation);
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

    private void SpawnFirework(Point center, double radius, int count, Color color, bool inner = false)
    {
        for (int i = 0; i < count; i++)
        {
            double angle = i * Math.PI * 2 / count + (inner ? Math.PI / count : 0) + (_random.NextDouble() - 0.5) * 0.06;
            double speed = radius * (1.7 + _random.NextDouble() * 0.35);
            double size = (inner ? 3.5 : 4.5) + _random.NextDouble() * 2;
            double life = (inner ? 1.1 : 1.45) + _random.NextDouble() * 0.25;
            var sparkColor = i % 6 == 0 && !inner ? ConfettiColors[(i / 6) % 5] : color;
            var brush = new SolidColorBrush(sparkColor);
            var trail = new Polyline
            {
                Stroke = brush,
                StrokeThickness = inner ? 1.8 : 2.3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            EffectCanvas.Children.Add(trail);
            var head = AddEffect(new Ellipse
            {
                Width = size,
                Height = size,
                Fill = Brushes.LightYellow,
                Stroke = brush,
                StrokeThickness = 1.5
            }, center);
            var spark = new FireworkSpark
            {
                Head = head,
                Trail = trail,
                Position = center,
                Velocity = new Vector(Math.Cos(angle) * speed, Math.Sin(angle) * speed),
                Life = life,
                MaxLife = life
            };
            spark.History.Enqueue(center);
            _fireworkSparks.Add(spark);
        }
    }

    private void UpdateFireworks(double dt)
    {
        for (int i = _fireworkSparks.Count - 1; i >= 0; i--)
        {
            var spark = _fireworkSparks[i];
            spark.Life -= dt;
            if (spark.Life <= 0)
            {
                EffectCanvas.Children.Remove(spark.Head);
                EffectCanvas.Children.Remove(spark.Trail);
                _fireworkSparks.RemoveAt(i);
                continue;
            }
            double drag = Math.Exp(-1.6 * dt);
            spark.Velocity = new Vector(spark.Velocity.X * drag, spark.Velocity.Y * drag + 58 * dt);
            spark.Position += spark.Velocity * dt;
            spark.History.Enqueue(spark.Position);
            if (spark.History.Count > 14) spark.History.Dequeue();
            spark.Trail.Points = new PointCollection(spark.History);
            Canvas.SetLeft(spark.Head, spark.Position.X - spark.Head.Width / 2);
            Canvas.SetTop(spark.Head, spark.Position.Y - spark.Head.Height / 2);
            double opacity = Math.Clamp(spark.Life / (spark.MaxLife * 0.65), 0, 1);
            spark.Head.Opacity = opacity;
            spark.Trail.Opacity = opacity * 0.85;
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
        _clapping = false;
        _clapClock.Reset();
        if (!_rendering) return;
        CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_clapping) UpdateClapping();
        var now = DateTime.UtcNow;
        double dt = Math.Min((now - _lastFrame).TotalSeconds, 0.05);
        _lastFrame = now;
        UpdateFireworks(dt);

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
        ApplyEffectAnimation(target, property, animation);
    }

    private void ApplyEffectAnimation(DependencyObject target, DependencyProperty property, AnimationTimeline animation)
    {
        if (target is UIElement element) element.BeginAnimation(property, animation);
        else if (target is Animatable animatable) animatable.BeginAnimation(property, animation);
        else throw new InvalidOperationException(LocalizationService.T("完成特效的动画目标不支持属性动画。"));
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
        _fireworkSparks.Clear();
        _activeStamp = null;
        EffectCanvas.Children.Clear();

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
        ApplauseSpark.RenderTransform = Transform.Identity;
        foreach (var contour in _clapContours)
            contour.SetPose(1, 0);
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
