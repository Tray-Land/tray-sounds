// Synthesizes the stock sounds into Assets\Sounds as 16-bit mono WAV.
// Run from the repo root: dotnet run scripts\GenerateStockSounds.cs
// Everything is generated from code (no samples), so the sounds carry no third-party license.

const int Rate = 44100;
string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Sounds");
Directory.CreateDirectory(outDir);
var rng = new Random(20261003);

Write("ding", Ding());
Write("buzzer", Buzzer());
Write("correct", Correct());
Write("laser", Laser());

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
