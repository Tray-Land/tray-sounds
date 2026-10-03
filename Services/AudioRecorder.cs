using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace TraySounds.Services;

/// <summary>
/// Records the default microphone to an .m4a in the app's temp folder. One instance per recording;
/// the microphone is released as soon as the recording stops.
/// </summary>
internal sealed class AudioRecorder : IAsyncDisposable
{
    public static readonly TimeSpan MaxLength = TimeSpan.FromSeconds(60);

    private MediaCapture? _capture;
    private LowLagMediaRecording? _recording;

    /// <summary>Path of the recorded file once <see cref="StartAsync"/> has run.</summary>
    public string? FilePath { get; private set; }

    public bool IsRecording => _recording is not null;

    /// <summary>Starts recording. Throws <see cref="UnauthorizedAccessException"/> when microphone access is off.</summary>
    public async Task StartAsync()
    {
        StorageFile file = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(
            $"recording-{DateTime.Now:yyyyMMdd-HHmmss}.m4a", CreationCollisionOption.GenerateUniqueName);
        FilePath = file.Path;

        _capture = new MediaCapture();
        try
        {
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio,
                MediaCategory = MediaCategory.Media,
            });

            _recording = await _capture.PrepareLowLagRecordToStorageFileAsync(
                MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High), file);
            await _recording.StartAsync();
        }
        catch
        {
            await DisposeAsync();
            TryDelete(FilePath);
            FilePath = null;
            throw;
        }
    }

    /// <summary>Stops recording and releases the microphone; the file at <see cref="FilePath"/> is complete.</summary>
    public async Task StopAsync()
    {
        if (_recording is not null)
        {
            try
            {
                await _recording.StopAsync();
                await _recording.FinishAsync();
            }
            finally
            {
                _recording = null;
            }
        }

        _capture?.Dispose();
        _capture = null;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        catch
        {
            _capture?.Dispose();
            _capture = null;
        }
    }

    public static void TryDelete(string? path)
    {
        try
        {
            if (path is not null)
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temp files get cleaned up by Windows eventually.
        }
    }
}
