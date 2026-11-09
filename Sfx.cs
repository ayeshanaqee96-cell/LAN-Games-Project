using System.Media;

namespace LanGames;

/// <summary>
/// All sound effects are synthesised in memory (WAV), so the project needs no audio files.
/// Sfx.Play("dice"), Sfx.Play("step") ...
/// </summary>
public static class Sfx
{
    private const int Rate = 22050;
    private static readonly Dictionary<string, SoundPlayer> bank = new();
    public static bool Muted { get; set; }

    static Sfx()
    {
        Add("click", Tone(1000, 45, .45, 7));
        Add("dice", Rattle());
        Add("step", Tone(520, 45, .5, 8));
        Add("move", Cat(Noise(25, .45, 6), Tone(190, 90, .7, 7)));
        Add("capture", Tone(700, 280, .6, 3, 120));
        Add("home", Cat(Tone(523, 90, .5, 3), Tone(659, 90, .5, 3), Tone(784, 170, .5, 3)));
        Add("win", Cat(Tone(523, 110, .5, 2), Tone(659, 110, .5, 2), Tone(784, 110, .5, 2), Tone(1047, 380, .55, 2.5)));
        Add("check", Cat(Tone(880, 90, .5, 3), Silence(40), Tone(880, 140, .5, 3)));
        Add("chat", Cat(Tone(880, 70, .4, 4), Tone(1175, 110, .4, 4)));
        Add("error", Tone(160, 180, .6, 3));
    }

    public static void Play(string name)
    {
        if (Muted || !bank.TryGetValue(name, out var player)) return;
        try { player.Play(); } catch { }
    }

    // ----- synthesis helpers -----
    private static List<short> Tone(double freq, int ms, double vol, double decay, double freqEnd = 0)
    {
        int n = Rate * ms / 1000;
        var list = new List<short>(n);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double f = freqEnd > 0 ? freq + (freqEnd - freq) * t : freq;
            phase += 2 * Math.PI * f / Rate;
            list.Add((short)(Math.Sin(phase) * Math.Exp(-decay * t) * vol * short.MaxValue));
        }
        return list;
    }

    private static List<short> Noise(int ms, double vol, double decay)
    {
        var rnd = new Random(7);
        int n = Rate * ms / 1000;
        var list = new List<short>(n);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            list.Add((short)((rnd.NextDouble() * 2 - 1) * Math.Exp(-decay * t) * vol * short.MaxValue));
        }
        return list;
    }

    private static List<short> Silence(int ms) => new(new short[Rate * ms / 1000]);

    private static List<short> Rattle()
    {
        var parts = new List<List<short>>();
        for (int i = 0; i < 7; i++)
        {
            parts.Add(Noise(30, .5, 4));
            parts.Add(Tone(300 + i * 40, 25, .3, 6));
            parts.Add(Silence(22 + i * 8));
        }
        parts.Add(Tone(260, 80, .5, 5));
        return Cat(parts.ToArray());
    }

    private static List<short> Cat(params List<short>[] parts)
    {
        var all = new List<short>();
        foreach (var p in parts) all.AddRange(p);
        return all;
    }

    private static void Add(string name, List<short> samples)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        int dataLen = samples.Count * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataLen); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataLen);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        ms.Position = 0;
        var player = new SoundPlayer(ms);
        player.Load();
        bank[name] = player;
    }
}
