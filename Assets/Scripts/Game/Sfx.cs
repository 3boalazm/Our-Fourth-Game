using UnityEngine;

/// <summary>Sound effects and a little music loop, all synthesised at start-up (no audio files).</summary>
public sealed class Sfx
{
    const int Rate = 22050;
    readonly AudioSource fx, music;
    readonly AudioClip capture, bigCapture, crash, cut, tick, go, win, lose, turn, join;
    public bool Muted { get; private set; }

    public Sfx(GameObject host)
    {
        fx = host.AddComponent<AudioSource>();
        fx.playOnAwake = false;
        music = host.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.loop = true;
        music.volume = 0.22f;

        capture = Notes("capture", new[] { 72f, 76f, 79f }, 0.07f, 0.16f, Wave.Triangle, 0.5f);
        bigCapture = Notes("big", new[] { 72f, 76f, 79f, 84f, 88f }, 0.07f, 0.28f, Wave.Triangle, 0.55f);
        crash = Noise("crash", 0.5f, 900f, 0.7f);
        cut = Notes("cut", new[] { 84f, 79f }, 0.05f, 0.12f, Wave.Square, 0.3f);
        tick = Notes("tick", new[] { 69f }, 0.0f, 0.12f, Wave.Square, 0.25f);
        go = Notes("go", new[] { 81f }, 0.0f, 0.35f, Wave.Square, 0.3f);
        win = Notes("win", new[] { 72f, 76f, 79f, 84f, 79f, 84f }, 0.12f, 0.4f, Wave.Triangle, 0.5f);
        lose = Notes("lose", new[] { 67f, 64f, 60f }, 0.16f, 0.4f, Wave.Triangle, 0.45f);
        turn = Notes("turn", new[] { 90f }, 0.0f, 0.03f, Wave.Sine, 0.12f);
        join = Notes("join", new[] { 76f, 83f }, 0.06f, 0.15f, Wave.Sine, 0.35f);
        music.clip = Music();
    }

    enum Wave { Sine, Triangle, Square }

    static float Osc(Wave w, float phase)
    {
        float t = phase - Mathf.Floor(phase);
        switch (w)
        {
            case Wave.Triangle: return 4f * Mathf.Abs(t - 0.5f) - 1f;
            case Wave.Square: return t < 0.5f ? 0.6f : -0.6f;
            default: return Mathf.Sin(t * Mathf.PI * 2f);
        }
    }

    static float Hz(float midi) { return 440f * Mathf.Pow(2f, (midi - 69f) / 12f); }

    // A quick arpeggio: each note starts `gap` after the previous and rings for `length`.
    static AudioClip Notes(string name, float[] notes, float gap, float length, Wave w, float volume)
    {
        int total = (int)((gap * (notes.Length - 1) + length + 0.05f) * Rate);
        var data = new float[total];
        for (int n = 0; n < notes.Length; n++)
        {
            int start = (int)(gap * n * Rate);
            int len = (int)(length * Rate);
            float f = Hz(notes[n]);
            for (int i = 0; i < len && start + i < total; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t * 200f) * Mathf.Exp(-t * 5f / length);
                data[start + i] += Osc(w, f * t) * env * volume;
            }
        }
        var clip = AudioClip.Create(name, total, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip Noise(string name, float length, float cutoff, float volume)
    {
        int total = (int)(length * Rate);
        var data = new float[total];
        var rng = new System.Random(3);
        float y = 0f;
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)Rate;
            float k = Mathf.Clamp01(cutoff * (1f - t / length) / Rate * 6.28f);
            y += ((float)rng.NextDouble() * 2f - 1f - y) * k;
            float boom = Mathf.Sin(t * Mathf.PI * 2f * Mathf.Lerp(120f, 40f, t / length));
            data[i] = (y * 0.8f + boom * 0.5f) * Mathf.Exp(-t * 6f) * volume;
        }
        var clip = AudioClip.Create(name, total, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // 8 bars of a bouncy loop: bass on the beat, arpeggio on top.
    static AudioClip Music()
    {
        float bpm = 118f;
        float beat = 60f / bpm;
        int bars = 8;
        int total = (int)(bars * 4 * beat * Rate);
        var data = new float[total];
        int[] roots = { 60, 60, 57, 57, 53, 53, 55, 55 }; // C C Am Am F F G G
        for (int bar = 0; bar < bars; bar++)
        {
            int root = roots[bar];
            for (int b = 0; b < 4; b++)
            {
                // bass
                AddTone(data, (bar * 4 + b) * beat, beat * 0.9f, Hz(root - 12 + (b == 2 ? 7 : 0)), Wave.Triangle, 0.32f);
                // arpeggio in eighths
                int[] arp = { 0, 4, 7, 12 };
                for (int e = 0; e < 2; e++)
                {
                    int note = root + arp[(b * 2 + e) % 4] + (root == 57 ? -1 : 0) * 0;
                    if (root == 57 && arp[(b * 2 + e) % 4] == 4) note = root + 3; // minor third on Am
                    AddTone(data, (bar * 4 + b + e * 0.5f) * beat, beat * 0.4f, Hz(note + 12), Wave.Square, 0.07f);
                }
                // soft hat
                AddNoise(data, (bar * 4 + b + 0.5f) * beat, 0.03f, 0.05f);
            }
        }
        var clip = AudioClip.Create("music", total, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static void AddTone(float[] data, float at, float length, float f, Wave w, float volume)
    {
        int start = (int)(at * Rate);
        int len = (int)(length * Rate);
        for (int i = 0; i < len && start + i < data.Length; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Min(1f, t * 300f) * Mathf.Min(1f, (length - t) * 40f) * Mathf.Exp(-t * 2.5f);
            data[start + i] += Osc(w, f * t) * env * volume;
        }
    }

    static void AddNoise(float[] data, float at, float length, float volume)
    {
        int start = (int)(at * Rate);
        int len = (int)(length * Rate);
        var rng = new System.Random(start);
        for (int i = 0; i < len && start + i < data.Length; i++)
        {
            float t = i / (float)Rate;
            data[start + i] += ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 90f) * volume;
        }
    }

    public void SetMuted(bool m)
    {
        Muted = m;
        fx.mute = m;
        music.mute = m;
    }

    public void PlayMusic(bool on)
    {
        if (on && !music.isPlaying) music.Play();
        else if (!on && music.isPlaying) music.Stop();
    }

    void Play(AudioClip c, float volume = 1f)
    {
        if (c != null) fx.PlayOneShot(c, volume);
    }

    public void Capture(bool big) { Play(big ? bigCapture : capture); }
    public void Crash() { Play(crash); }
    public void Cut() { Play(cut); }
    public void Tick() { Play(tick); }
    public void Go() { Play(go); }
    public void Win() { Play(win); }
    public void Lose() { Play(lose); }
    public void Turn() { Play(turn, 0.6f); }
    public void Join() { Play(join); }
}
