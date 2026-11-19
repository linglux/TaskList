using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using TaskList.Models;

namespace TaskList.Services;

/// <summary>纯代码合成音效，不引用任何外部音频资源，也不联网。</summary>
public static class SoundService
{
    public static bool Enabled { get; set; } = true;

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

    private static readonly GCHandle ChimePin = GCHandle.Alloc(Chime, GCHandleType.Pinned);
    private static readonly GCHandle MidChimePin = GCHandle.Alloc(MidChime, GCHandleType.Pinned);
    private static readonly GCHandle TickPin = GCHandle.Alloc(Tick, GCHandleType.Pinned);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    public static void Play(TaskWeight weight)
    {
        if (!Enabled) return;
        switch (weight)
        {
            case TaskWeight.Heavy:
                PlaySound(ChimePin.AddrOfPinnedObject(), IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
                break;
            case TaskWeight.Medium:
                PlaySound(MidChimePin.AddrOfPinnedObject(), IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
                break;
            case TaskWeight.Light:
                PlaySound(TickPin.AddrOfPinnedObject(), IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);
                break;
        }
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
