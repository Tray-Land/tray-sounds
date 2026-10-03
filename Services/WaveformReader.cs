using NAudio.Wave;

namespace TraySounds.Services;

/// <summary>A clip's length and its peak levels, ready to draw.</summary>
internal sealed record Waveform(TimeSpan Duration, float[] Peaks);

/// <summary>Decodes a clip with Media Foundation (any format the app accepts) and reduces it to peaks.</summary>
internal static class WaveformReader
{
    public static Task<Waveform> ReadAsync(string path, int buckets, CancellationToken cancel = default) =>
        Task.Run(() =>
        {
            using MediaFoundationReader reader = new(path);
            ISampleProvider samples = reader.ToSampleProvider();
            int channels = samples.WaveFormat.Channels;

            // Track the peak per bucket while streaming, so a long clip is never held in memory.
            long totalFrames = (long)(reader.TotalTime.TotalSeconds * samples.WaveFormat.SampleRate);
            float[] peaks = new float[buckets];
            float[] chunk = new float[samples.WaveFormat.SampleRate * channels / 4];
            long frame = 0;
            int read;
            while ((read = samples.Read(chunk, 0, chunk.Length)) > 0)
            {
                cancel.ThrowIfCancellationRequested();
                for (int i = 0; i + channels <= read; i += channels, frame++)
                {
                    float peak = 0;
                    for (int c = 0; c < channels; c++)
                    {
                        peak = Math.Max(peak, Math.Abs(chunk[i + c]));
                    }

                    int bucket = totalFrames <= 0 ? 0 : (int)Math.Min(frame * buckets / totalFrames, buckets - 1);
                    peaks[bucket] = Math.Max(peaks[bucket], peak);
                }
            }

            // Run the streamed peaks through the policy so scaling matches what's tested.
            return new Waveform(reader.TotalTime, SoundTrimPolicy.Peaks(peaks, buckets));
        }, cancel);
}
