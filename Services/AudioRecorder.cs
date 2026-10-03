using NAudio.CoreAudioApi;
using NAudio.Wave;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace TraySounds.Services;

/// <summary>What a recording captures.</summary>
public enum RecordingSource
{
    Microphone,
    SystemAudio,
}

/// <summary>
/// Records the default microphone (.m4a) or whatever the default output device is playing (.wav, WASAPI
/// loopback) to the app's temp folder. One instance per recording; the device is released as soon as
/// the recording stops.
/// </summary>
internal sealed class AudioRecorder(RecordingSource source = RecordingSource.Microphone) : IAsyncDisposable
{
    public static readonly TimeSpan MaxLength = TimeSpan.FromSeconds(60);

    private MediaCapture? _capture;
    private LowLagMediaRecording? _recording;

    private WasapiLoopbackCapture? _loopback;
    private WaveFileWriter? _writer;
    private TaskCompletionSource? _loopbackStopped;

    private MMDevice? _meterDevice;

    /// <summary>Path of the recorded file once <see cref="StartAsync"/> has run.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Friendly name of the device being metered (the default microphone or output), if known.</summary>
    public string? DeviceName { get; private set; }

    /// <summary>Current peak level of the source, 0 to 1. Zero when it can't be read.</summary>
    public float Level
    {
        get
        {
            try
            {
                return _meterDevice?.AudioMeterInformation.MasterPeakValue ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public bool IsRecording => _recording is not null || _loopback is not null;

    /// <summary>Starts recording. Throws <see cref="UnauthorizedAccessException"/> when microphone access is off.</summary>
    public async Task StartAsync()
    {
        string extension = source == RecordingSource.SystemAudio ? "wav" : "m4a";
        StorageFile file = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(
            $"recording-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}", CreationCollisionOption.GenerateUniqueName);
        FilePath = file.Path;

        try
        {
            if (source == RecordingSource.SystemAudio)
            {
                StartLoopback(file.Path);
            }
            else
            {
                await StartMicrophoneAsync(file);
            }

            OpenMeter();
        }
        catch
        {
            await DisposeAsync();
            TryDelete(FilePath);
            FilePath = null;
            throw;
        }
    }

    private async Task StartMicrophoneAsync(StorageFile file)
    {
        _capture = new MediaCapture();
        await _capture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            StreamingCaptureMode = StreamingCaptureMode.Audio,
            MediaCategory = MediaCategory.Media,
        });

        _recording = await _capture.PrepareLowLagRecordToStorageFileAsync(
            MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High), file);
        await _recording.StartAsync();
    }

    /// <summary>Opens the default device's peak meter so the UI can show what is being picked up. Best effort.</summary>
    private void OpenMeter()
    {
        try
        {
            using MMDeviceEnumerator enumerator = new();
            DataFlow flow = source == RecordingSource.SystemAudio ? DataFlow.Render : DataFlow.Capture;
            _meterDevice = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
            DeviceName = _meterDevice.FriendlyName;
        }
        catch
        {
            _meterDevice = null;
        }
    }

    private void StartLoopback(string path)
    {
        WasapiLoopbackCapture loopback = new();
        WaveFileWriter writer = new(path, loopback.WaveFormat);
        TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        loopback.DataAvailable += (_, e) => writer.Write(e.Buffer, 0, e.BytesRecorded);
        loopback.RecordingStopped += (_, e) =>
        {
            if (e.Exception is null)
            {
                stopped.TrySetResult();
            }
            else
            {
                stopped.TrySetException(e.Exception);
            }
        };

        _loopback = loopback;
        _writer = writer;
        _loopbackStopped = stopped;
        loopback.StartRecording();
    }

    /// <summary>Stops recording and releases the device; the file at <see cref="FilePath"/> is complete.</summary>
    public async Task StopAsync()
    {
        _meterDevice?.Dispose();
        _meterDevice = null;

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

        if (_loopback is { } loopback)
        {
            long bytes = 0;
            try
            {
                loopback.StopRecording();
                await (_loopbackStopped?.Task ?? Task.CompletedTask);
                bytes = _writer?.Length ?? 0;
            }
            finally
            {
                _writer?.Dispose();
                loopback.Dispose();
                _writer = null;
                _loopback = null;
                _loopbackStopped = null;
            }

            // Loopback only delivers data while something is playing.
            if (bytes == 0)
            {
                throw new InvalidOperationException("No sound was playing on this PC while recording.");
            }
        }
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
            _writer?.Dispose();
            _writer = null;
            _loopback?.Dispose();
            _loopback = null;
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
