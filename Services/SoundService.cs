using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Threading;
using TaskList.Models;

namespace TaskList.Services;

/// <summary>播放内置合成音效或用户导入的 MP3；由 WPF UI 线程调用。</summary>
public static class SoundService
{
    public const double MaxDurationSeconds = 10;
    public static event Action<string>? PlaybackFailed;

    private static bool _enabled = true;
    private static bool _disposed;
    private static MediaPlayer? _player;
    private static DispatcherTimer? _playbackTimer;
    private static readonly Dictionary<TaskWeight, string?> CustomSounds = new();

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) Stop();
        }
    }

    private const uint SndMemory = 0x0004;
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;

    private static readonly byte[] Chime = BuildSound(
        new[] { 523.25, 659.25, 783.99, 1046.50 },
        new[] { 0.0, 0.09, 0.18, 0.30 }, 1100, 4.2);

    private static readonly byte[] MidChime = BuildSound(
        new[] { 659.25, 830.61, 987.77 }, new[] { 0.0, 0.08, 0.17 }, 620, 6.5);

    private static readonly byte[] Tick = BuildSound(
        new[] { 880.0, 1318.51 }, new[] { 0.0, 0.05 }, 320, 12.0);

    private static GCHandle ChimePin = GCHandle.Alloc(Chime, GCHandleType.Pinned);
    private static GCHandle MidChimePin = GCHandle.Alloc(MidChime, GCHandleType.Pinned);
    private static GCHandle TickPin = GCHandle.Alloc(Tick, GCHandleType.Pinned);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    public static void SetCustomSound(TaskWeight weight, string? path)
    {
        Stop();
        CustomSounds[weight] = path;
    }

    public static void Play(TaskWeight weight)
    {
        if (Enabled) PlayCore(weight);
    }

    public static void Preview(TaskWeight weight) => PlayCore(weight);

    public static void Stop()
    {
        _playbackTimer?.Stop();
        var player = _player;
        _player = null;
        player?.Close();
        PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
    }

    public static void Cleanup()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        if (ChimePin.IsAllocated) ChimePin.Free();
        if (MidChimePin.IsAllocated) MidChimePin.Free();
        if (TickPin.IsAllocated) TickPin.Free();
        CustomSounds.Clear();
        PlaybackFailed = null;
    }

    public static async Task<TimeSpan> ValidateMp3Async(string path, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择 MP3 文件。");

        var player = new MediaPlayer();
        var completion = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.MediaOpened += (_, _) =>
        {
            if (!player.HasAudio || !player.NaturalDuration.HasTimeSpan)
                completion.TrySetException(new InvalidOperationException("无法读取这段音频的时长，请选择其他 MP3。"));
            else
                completion.TrySetResult(player.NaturalDuration.TimeSpan);
        };
        player.MediaFailed += (_, _) => completion.TrySetException(
            new InvalidOperationException("无法解码这段 MP3，请确认文件有效且系统媒体组件可用。"));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            player.Open(new Uri(Path.GetFullPath(path), UriKind.Absolute));
            var duration = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            if (duration <= TimeSpan.Zero || duration.TotalSeconds > MaxDurationSeconds)
                throw new InvalidOperationException("请选择时长大于 0 且不超过 10 秒的 MP3。此文件未导入。");
            return duration;
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("读取 MP3 超时，请选择其他文件或检查系统媒体组件。");
        }
        finally
        {
            player.Close();
        }
    }

    private static void PlayCore(TaskWeight weight)
    {
        if (_disposed) return;
        Stop();
        if (!CustomSounds.TryGetValue(weight, out var path) || string.IsNullOrWhiteSpace(path))
        {
            PlayDefault(weight);
            return;
        }
        if (!File.Exists(path))
        {
            Fallback(weight, "自定义音频文件不存在，已使用内置音效。请在设置中重新导入。");
            return;
        }

        try
        {
            var player = new MediaPlayer { Volume = 1 };
            _player = player;
            player.MediaOpened += (_, _) =>
            {
                if (!ReferenceEquals(_player, player)) return;
                if (!player.HasAudio || !player.NaturalDuration.HasTimeSpan ||
                    player.NaturalDuration.TimeSpan <= TimeSpan.Zero ||
                    player.NaturalDuration.TimeSpan.TotalSeconds > MaxDurationSeconds)
                {
                    Fallback(weight, "自定义音频无效或超过 10 秒，已使用内置音效。");
                    return;
                }
                try
                {
                    _playbackTimer?.Stop();
                    player.Play();
                    _playbackTimer?.Start();
                }
                catch (Exception)
                {
                    Fallback(weight, "自定义 MP3 无法播放，已使用内置音效。");
                }
            };
            player.MediaEnded += (_, _) => { if (ReferenceEquals(_player, player)) Stop(); };
            player.MediaFailed += (_, _) =>
            {
                if (ReferenceEquals(_player, player))
                    Fallback(weight, "自定义 MP3 无法播放，已使用内置音效。请在设置中重新导入。");
            };
            _playbackTimer ??= CreatePlaybackTimer();
            _playbackTimer.Start();
            player.Open(new Uri(Path.GetFullPath(path), UriKind.Absolute));
        }
        catch (Exception)
        {
            Fallback(weight, "自定义 MP3 无法打开，已使用内置音效。");
        }
    }

    private static DispatcherTimer CreatePlaybackTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(MaxDurationSeconds) };
        timer.Tick += (_, _) => Stop();
        return timer;
    }

    private static void Fallback(TaskWeight weight, string message)
    {
        Stop();
        PlayDefault(weight);
        PlaybackFailed?.Invoke(message);
    }

    private static void PlayDefault(TaskWeight weight)
    {
        if (_disposed) return;
        var handle = weight switch
        {
            TaskWeight.Heavy => ChimePin,
            TaskWeight.Medium => MidChimePin,
            _ => TickPin
        };
        PlaySound(handle.AddrOfPinnedObject(), IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
    }

    private static byte[] BuildSound(double[] notes, double[] offsets, int durationMs, double decay)
    {
        const int sampleRate = 44100;
        int samples = sampleRate * durationMs / 1000;
        int dataLength = samples * sizeof(short);

        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);                 // PCM
        writer.Write((short)1);                 // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / sampleRate;
            double value = 0;
            for (int n = 0; n < notes.Length; n++)
            {
                double dt = t - offsets[n];
                if (dt < 0) continue;
                double envelope = Math.Exp(-dt * decay) * (1 - Math.Exp(-dt * 80));
                value += 0.24 * envelope *
                         (Math.Sin(2 * Math.PI * notes[n] * dt) +
                          0.22 * Math.Sin(4 * Math.PI * notes[n] * dt));
            }
            writer.Write((short)(Math.Clamp(value, -1, 1) * short.MaxValue * 0.65));
        }

        writer.Flush();
        return stream.ToArray();
    }
}
