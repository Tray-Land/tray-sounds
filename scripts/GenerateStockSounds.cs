// Synthesizes the stock sounds into Assets\Sounds as 16-bit mono WAV.
// Run from the repo root: dotnet run scripts\GenerateStockSounds.cs
// Everything is generated from code (no samples), so the sounds carry no third-party license.

const int Rate = 44100;
string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Sounds");
Directory.CreateDirectory(outDir);
var rng = new Random(20261003);

Write("ding", Ding());
Write("tada", Tada());
Write("rimshot", Rimshot());
Write("sad-trombone", SadTrombone());
Write("airhorn", Airhorn());
Write("buzzer", Buzzer());
Write("correct", Correct());
Write("laser", Laser());
Write("boing", Boing());
Write("whoosh", Whoosh());
Write("applause", Applause());
Write("crickets", Crickets());

void Write(string name, float[] samples)
{
    Normalize(samples, 0.89f);
    FadeEdges(samples, 0.004);
    string path = Path.Combine(outDir, name + ".wav");
    using var stream = File.Create(path);
    using var w = new BinaryWriter(stream);
    int dataBytes = samples.Length * 2;
    w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
    w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
    w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
    w.Write("data"u8); w.Write(dataBytes);
    foreach (float s in samples)
    {
        w.Write((short)Math.Clamp(Math.Round(s * short.MaxValue), short.MinValue, short.MaxValue));
    }

    Console.WriteLine($"{name,-14} {samples.Length / (double)Rate:0.00}s  {new FileInfo(path).Length / 1024} KB");
}

float[] Buffer(double seconds) => new float[(int)(seconds * Rate)];

static double Saw(double phase) => 2 * (phase - Math.Floor(phase + 0.5));

static double Square(double phase) => phase - Math.Floor(phase) < 0.5 ? 1 : -1;

static double Midi(double note) => 440 * Math.Pow(2, (note - 69) / 12);

double Noise() => (rng.NextDouble() * 2) - 1;

// Attack/decay/sustain/release envelope; t and length in seconds.
static double Adsr(double t, double length, double a, double d, double s, double r)
{
    if (t < 0 || t > length) return 0;
    if (t < a) return t / a;
    if (t < a + d) return 1 - ((1 - s) * (t - a) / d);
    if (t > length - r) return s * Math.Max(0, (length - t) / r);
    return s;
}

// Additive bell: inharmonic partials, the higher ones dying faster.
float[] Ding()
{
    var b = Buffer(2.2);
    (double ratio, double amp, double decay)[] partials =
        [(1, 1, 1.6), (2.0, 0.6, 1.1), (2.76, 0.45, 0.8), (5.4, 0.25, 0.4), (8.93, 0.12, 0.25)];
    for (int i = 0; i < b.Length; i++)
    {
        double t = i / (double)Rate;
        double v = 0;
        foreach (var (ratio, amp, decay) in partials)
        {
            v += amp * Math.Sin(2 * Math.PI * 1318.5 * ratio * t) * Math.Exp(-t * 3 / decay);
        }

        b[i] = (float)(v * Math.Min(1, t / 0.002));
    }

    return b;
}

// Brass-ish fanfare: quick pickup notes into a held major chord.
float[] Tada()
{
    var b = Buffer(1.9);
    (double start, double length, double[] notes)[] hits =
        [(0, 0.12, [67]), (0.14, 0.12, [67]), (0.30, 1.55, [60, 64, 67, 72])];
    foreach (var (start, length, notes) in hits)
    {
        foreach (double note in notes)
        {
            double f = Midi(note);
            for (int i = 0; i < (int)(length * Rate); i++)
            {
                double t = i / (double)Rate;
                double vib = 1 + (0.004 * Math.Sin(2 * Math.PI * 5.5 * t) * Math.Min(1, t / 0.4));
                double phase = f * vib * t;
                double tone = (0.6 * Saw(phase)) + (0.4 * Math.Sin(2 * Math.PI * phase));
                // Brass "blat": brighter at the attack.
                double bright = 1 + (0.6 * Math.Exp(-t * 18));
                int idx = (int)(start * Rate) + i;
                if (idx < b.Length) b[idx] += (float)(tone * bright * Adsr(t, length, 0.02, 0.1, 0.75, 0.25) * 0.3);
            }
        }
    }

    return LowPass(b, 3200);
}

// Ba-dum-tss: two tom hits and a cymbal splash.
float[] Rimshot()
{
    var b = Buffer(1.8);
    void Tom(double at, double f0, double length)
    {
        double phase = 0;
        for (int i = 0; i < (int)(length * Rate); i++)
        {
            double t = i / (double)Rate;
            double f = f0 * (1 + (0.6 * Math.Exp(-t * 30)));
            phase += f / Rate;
            double v = (Math.Sin(2 * Math.PI * phase) * Math.Exp(-t * 9)) + (Noise() * 0.3 * Math.Exp(-t * 60));
            int idx = (int)(at * Rate) + i;
            if (idx < b.Length) b[idx] += (float)(v * 0.8);
        }
    }

    Tom(0, 180, 0.35);
    Tom(0.17, 120, 0.45);
    var cymbal = Buffer(1.4);
    double hp = 0, last = 0;
    for (int i = 0; i < cymbal.Length; i++)
    {
        double t = i / (double)Rate;
        double n = Noise();
        hp = 0.92 * (hp + n - last); // one-pole high-pass for a bright splash
        last = n;
        double metal = Square(5400 * t) * 0.15 + Square(7900 * t) * 0.1;
        cymbal[i] = (float)((hp + metal) * Math.Exp(-t * 3.2) * Math.Min(1, t / 0.003));
    }

    for (int i = 0; i < cymbal.Length; i++)
    {
        int idx = (int)(0.42 * Rate) + i;
        if (idx < b.Length) b[idx] += cymbal[i] * 0.55f;
    }

    return b;
}

// Wah-wah-wah-waaah: four descending notes, the last one wobbling.
float[] SadTrombone()
{
    var b = Buffer(2.9);
    (double start, double length, double note)[] notes =
        [(0, 0.42, 58), (0.45, 0.42, 57), (0.9, 0.42, 56), (1.35, 1.5, 55)];
    double phase = 0;
    int cursor = 0;
    foreach (var (start, length, note) in notes)
    {
        bool last = note == 55;
        for (int i = 0; i < (int)(length * Rate); i++)
        {
            double t = i / (double)Rate;
            double bend = last ? 0.35 * Math.Sin(2 * Math.PI * 5 * t) * Math.Min(1, t / 0.3) : 0;
            double glide = -0.3 * Math.Min(1, t / length); // each note sags a little
            phase += Midi(note + bend + glide) / Rate;
            // A wah: brightness swells in and out with each note.
            double wah = 0.35 + (0.65 * Math.Sin(Math.PI * Math.Min(1, t / length)));
            double tone = (Saw(phase) * wah) + (Math.Sin(2 * Math.PI * phase) * (1 - wah));
            cursor = (int)(start * Rate) + i;
            if (cursor < b.Length) b[cursor] += (float)(tone * Adsr(t, length, 0.03, 0.05, 0.9, 0.12));
        }
    }

    return LowPass(b, 1800);
}

// Stacked, slightly detuned saws in three blasts.
float[] Airhorn()
{
    var b = Buffer(2.1);
    double[] freqs = [415, 418, 523, 527, 622];
    (double start, double length)[] blasts = [(0, 0.28), (0.36, 0.28), (0.72, 1.3)];
    foreach (var (start, length) in blasts)
    {
        for (int i = 0; i < (int)(length * Rate); i++)
        {
            double t = i / (double)Rate;
            double v = 0;
            foreach (double f in freqs) v += Saw(f * t * (1 - (0.03 * Math.Exp(-t * 25))));
            int idx = (int)(start * Rate) + i;
            if (idx < b.Length) b[idx] += (float)(v * Adsr(t, length, 0.01, 0.05, 0.85, 0.06) * 0.2);
        }
    }

    return LowPass(b, 4500);
}

// Game-show wrong answer.
float[] Buzzer()
{
    var b = Buffer(1.0);
    for (int i = 0; i < b.Length; i++)
    {
        double t = i / (double)Rate;
        double v = (Square(110 * t) * 0.6) + (Square(116.5 * t) * 0.4);
        b[i] = (float)(v * Adsr(t, 1.0, 0.005, 0.02, 0.9, 0.05) * 0.5);
    }

    return LowPass(b, 2500);
}

// Game-show right answer: two bright chimes going up.
float[] Correct()
{
    var b = Buffer(1.2);
    (double start, double note)[] chimes = [(0, 81), (0.13, 88)];
    foreach (var (start, note) in chimes)
    {
        double f = Midi(note);
        for (int i = 0; i < (int)((1.2 - start) * Rate); i++)
        {
            double t = i / (double)Rate;
            double v = (Math.Sin(2 * Math.PI * f * t) + (0.3 * Math.Sin(2 * Math.PI * f * 2 * t)) + (0.1 * Math.Sin(2 * Math.PI * f * 3 * t)))
                       * Math.Exp(-t * 4) * Math.Min(1, t / 0.003);
            int idx = (int)(start * Rate) + i;
            if (idx < b.Length) b[idx] += (float)(v * 0.5);
        }
    }

    return b;
}

// Pew: a fast exponential pitch drop.
float[] Laser()
{
    var b = Buffer(0.45);
    double phase = 0;
    for (int i = 0; i < b.Length; i++)
    {
        double t = i / (double)Rate;
        double f = 300 + (2600 * Math.Exp(-t * 11));
        phase += f / Rate;
        double v = (Square(phase) * 0.4) + (Math.Sin(2 * Math.PI * phase) * 0.6);
        b[i] = (float)(v * Math.Exp(-t * 4.5));
    }

    return LowPass(b, 6000);
}

// A spring: pitch wobbles around a rising center while it rings out.
float[] Boing()
{
    var b = Buffer(1.1);
    double phase = 0;
    for (int i = 0; i < b.Length; i++)
    {
        double t = i / (double)Rate;
        double f = (140 + (160 * Math.Min(1, t / 0.12))) * (1 + (0.35 * Math.Sin(2 * Math.PI * 14 * t) * Math.Exp(-t * 3)));
        phase += f / Rate;
        double v = Math.Sin(2 * Math.PI * phase) + (0.25 * Math.Sin(4 * Math.PI * phase));
        b[i] = (float)(v * Math.Exp(-t * 3.2) * Math.Min(1, t / 0.004));
    }

    return b;
}

// Band-passed noise sweeping up and back down.
float[] Whoosh()
{
    var b = Buffer(1.0);
    double low = 0, band = 0;
    for (int i = 0; i < b.Length; i++)
    {
        double t = i / (double)Rate;
        double x = t / 1.0;
        double center = 300 + (2200 * Math.Sin(Math.PI * x));
        double fc = 2 * Math.Sin(Math.PI * center / Rate); // state-variable filter
        double high = Noise() - low - (0.35 * band);
        band += fc * high;
        low += fc * band;
        double env = Math.Pow(Math.Sin(Math.PI * x), 2.2);
        b[i] = (float)(band * env);
    }

    return b;
}

// Hundreds of short, randomly placed clap bursts over a crowd rumble.
float[] Applause()
{
    var b = Buffer(3.2);
    for (int c = 0; c < 900; c++)
    {
        double at = rng.NextDouble() * 3.0;
        double density = Math.Min(1, at / 0.4) * (at > 2.4 ? Math.Max(0, (3.0 - at) / 0.6) : 1);
        if (rng.NextDouble() > density) continue;
        double gain = 0.3 + (rng.NextDouble() * 0.7);
        double decay = 120 + (rng.NextDouble() * 120);
        double hp = 0, last = 0;
        int start = (int)(at * Rate);
        for (int i = 0; i < (int)(0.04 * Rate); i++)
        {
            double t = i / (double)Rate;
            double n = Noise();
            hp = 0.8 * (hp + n - last);
            last = n;
            int idx = start + i;
            if (idx < b.Length) b[idx] += (float)(hp * gain * Math.Exp(-t * decay) * 0.35);
        }
    }

    return LowPass(b, 7000);
}

// Three chirps of a cricket, twice, with some night air.
float[] Crickets()
{
    var b = Buffer(2.4);
    double[] chirpStarts = [0.1, 0.28, 0.46, 1.3, 1.48, 1.66];
    foreach (double start in chirpStarts)
    {
        double length = 0.12;
        for (int i = 0; i < (int)(length * Rate); i++)
        {
            double t = i / (double)Rate;
            // The chirp is a 4.6 kHz tone gated into quick pulses.
            double pulse = Math.Max(0, Math.Sin(2 * Math.PI * 70 * t));
            double v = Math.Sin(2 * Math.PI * 4600 * t) * pulse * Math.Sin(Math.PI * t / length);
            int idx = (int)(start * Rate) + i;
            if (idx < b.Length) b[idx] += (float)(v * 0.6);
        }
    }

    double air = 0;
    for (int i = 0; i < b.Length; i++)
    {
        air = (0.995 * air) + (0.005 * Noise());
        b[i] += (float)(air * 0.6);
    }

    return b;
}

static float[] LowPass(float[] samples, double cutoff)
{
    double rc = 1 / (2 * Math.PI * cutoff);
    double alpha = (1.0 / Rate) / (rc + (1.0 / Rate));
    double y = 0;
    for (int pass = 0; pass < 2; pass++)
    {
        y = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            y += alpha * (samples[i] - y);
            samples[i] = (float)y;
        }
    }

    return samples;
}

static void Normalize(float[] samples, float peak)
{
    float max = samples.Max(Math.Abs);
    if (max <= 0) return;
    float gain = peak / max;
    for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
}

static void FadeEdges(float[] samples, double seconds)
{
    int n = Math.Min(samples.Length / 2, (int)(seconds * Rate));
    for (int i = 0; i < n; i++)
    {
        float g = i / (float)n;
        samples[i] *= g;
        samples[^(i + 1)] *= g;
    }
}
