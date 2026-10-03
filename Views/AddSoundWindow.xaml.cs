using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using TraySounds.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WinUIEx;

namespace TraySounds.Views;

/// <summary>How the add window should start: straight into recording, the file picker, or neither.</summary>
public enum AddSoundStart
{
    None,
    Record,
    ChooseFile,
}

/// <summary>
/// Records a clip or takes an audio file (picked or dropped), lets the user preview and name it,
/// then adds it to the library. A window rather than a flyout page because the file picker and the
/// microphone consent prompt both take focus, which would light-dismiss the flyout.
/// </summary>
public sealed partial class AddSoundWindow : WindowEx
{
    private const int MicrophoneDisabledHResult = unchecked((int)0x80070005); // E_ACCESSDENIED
    private const int NoCaptureDeviceHResult = unchecked((int)0xC00DABE0); // MF_E_NO_CAPTURE_DEVICES_AVAILABLE

    private readonly DispatcherQueueTimer _recordingTimer;
    private readonly Stopwatch _recordingClock = new();
    private AudioRecorder? _recorder;
    private MediaPlayer? _preview;

    // The chosen source: a temp recording (moved into the library on Add) or the user's own file (copied).
    private string? _sourcePath;
    private bool _sourceIsRecording;
    private bool _isBusy;
    private bool _isClosed;
    private bool _emojiChosen;
    private string _emoji = SoundLibrary.FileEmoji;

    private const int WaveBuckets = 96;
    private const double WaveformHeight = 48;

    // Edit mode: the window opens on an existing user sound instead of an empty source.
    private readonly Sound? _editing;
    private Waveform? _waveform;
    private CancellationTokenSource? _waveformLoad;
    private bool _settingTrim;

    public AddSoundWindow(Sound? editing = null)
    {
        _editing = editing;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        WindowPlacementService.CenterOnPrimary(this, 440, 600);
        IsMaximizable = false;
        IsMinimizable = false;
        IsResizable = false;

        RecordingLimit.Text = $"Recordings stop on their own after {SoundNamePolicy.FormatDuration(AudioRecorder.MaxLength)}.";
        _recordingTimer = DispatcherQueue.CreateTimer();
        _recordingTimer.Interval = TimeSpan.FromMilliseconds(50);
        BuildWaveform();
        _recordingTimer.Tick += RecordingTimer_Tick;

        EmojiText.Text = _emoji;
        EmojiPickerPanel picker = new();
        picker.EmojiPicked += (_, emoji) =>
        {
            _emojiChosen = true;
            SetEmoji(emoji);
            EmojiFlyout.Hide();
        };
        EmojiFlyout.Content = picker;

        WaveformHost.SizeChanged += (_, _) => DrawWaveform();
        Closed += OnClosed;

        if (editing is not null)
        {
            Title = "Edit sound";
            AppTitleBar.Title = "Edit sound";
            AddButton.Content = "Save";
            StartOverButton.Visibility = Visibility.Collapsed;
            _sourcePath = editing.FilePath; // the library's own copy: never deleted from here
            _sourceIsRecording = false;
            _emojiChosen = true;
            NameBox.Text = editing.Name;
            SetEmoji(editing.Emoji);
            ShowReady("Trim the sound, or change its name and emoji.", editing.Emoji);
            LoadWaveform(editing.FilePath, editing.Trim);
        }
    }

    /// <summary>Brings the window up, optionally jumping straight into recording or the picker.</summary>
    public void Start(AddSoundStart start)
    {
        Activate();
        this.BringToFront();
        if (_isBusy || _recorder is not null || _sourcePath is not null)
        {
            return; // already mid-way through adding something; don't throw that away
        }

        switch (start)
        {
            case AddSoundStart.Record:
                _ = StartRecordingAsync();
                break;
            case AddSoundStart.ChooseFile:
                _ = ChooseFileAsync();
                break;
        }
    }

    private void ShowEmpty()
    {
        EmptyPanel.Visibility = Visibility.Visible;
        RecordingPanel.Visibility = Visibility.Collapsed;
        ReadyPanel.Visibility = Visibility.Collapsed;
        UpdateAddButton();
    }

    private void ShowRecording()
    {
        EmptyPanel.Visibility = Visibility.Collapsed;
        RecordingPanel.Visibility = Visibility.Visible;
        ReadyPanel.Visibility = Visibility.Collapsed;
        StopRecordingButton.Focus(FocusState.Programmatic);
        UpdateAddButton();
    }

    private void ShowReady(string description, string emoji)
    {
        EmptyPanel.Visibility = Visibility.Collapsed;
        RecordingPanel.Visibility = Visibility.Collapsed;
        ReadyPanel.Visibility = Visibility.Visible;
        if (!_emojiChosen)
        {
            SetEmoji(emoji);
        }

        ReadyDescription.Text = description;
        NameBox.Focus(FocusState.Programmatic);
        NameBox.SelectAll();
        UpdateAddButton();
    }

    private void SetEmoji(string emoji)
    {
        _emoji = emoji;
        EmojiText.Text = emoji;
        ReadyEmoji.Text = emoji;
    }

    private void UpdateAddButton() =>
        AddButton.IsEnabled = !_isBusy && _sourcePath is not null && SoundNamePolicy.Clean(NameBox.Text).Length > 0;

    private void ShowError(string title, string message, string? settingsUri = null)
    {
        ErrorBar.Title = title;
        ErrorBar.Message = message;
        if (settingsUri is null)
        {
            ErrorBar.ActionButton = null;
        }
        else
        {
            HyperlinkButton link = new() { Content = "Open settings", NavigateUri = new Uri(settingsUri) };
            ErrorBar.ActionButton = link;
        }

        ErrorBar.IsOpen = true;
    }

    // Recording

    private async Task StartRecordingAsync()
    {
        ErrorBar.IsOpen = false;
        StopPreview();
        PlaybackService.StopAll(); // don't record the soundboard itself (or its own preview)
        _isBusy = true;
        RecordButton.IsEnabled = false;

        RecordingSource source = SourceBox.SelectedIndex == 1 ? RecordingSource.SystemAudio : RecordingSource.Microphone;
        AudioRecorder recorder = new(source);
        try
        {
            await recorder.StartAsync();
        }
        catch (Exception ex)
        {
            _isBusy = false;
            RecordButton.IsEnabled = true;
            if (source == RecordingSource.SystemAudio)
            {
                ShowError("Can't record system audio", ex.Message);
            }
            else if (ex is UnauthorizedAccessException || ex.HResult == MicrophoneDisabledHResult)
            {
                ShowError("Microphone access is off", "Allow apps to use the microphone in Windows Settings, then try again.", "ms-settings:privacy-microphone");
            }
            else if (ex.HResult == NoCaptureDeviceHResult)
            {
                ShowError("No microphone found", "Connect a microphone and try again.");
            }
            else
            {
                ShowError("Can't start recording", ex.Message);
            }

            ShowEmpty();
            return;
        }

        if (_isClosed)
        {
            await recorder.DisposeAsync();
            AudioRecorder.TryDelete(recorder.FilePath);
            return;
        }

        _recorder = recorder;
        _isBusy = false;
        RecordButton.IsEnabled = true;
        _recordingClock.Restart();
        RecordingTime.Text = SoundNamePolicy.FormatDuration(TimeSpan.Zero);
        ResetWaveform();
        RecordingDevice.Text = recorder.DeviceName is { } device
            ? $"{(source == RecordingSource.SystemAudio ? "Listening to" : "Listening on")} {device}"
            : (source == RecordingSource.SystemAudio ? "Listening to system audio" : "Listening on the microphone");
        _recordingTimer.Start();
        ShowRecording();
    }

    private const int BarCount = 48;
    private const double BarWidth = 3;
    private const double WaveHeight = 40;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];

    private void BuildWaveform()
    {
        double step = 240.0 / BarCount;
        for (int i = 0; i < BarCount; i++)
        {
            Rectangle bar = new()
            {
                Width = BarWidth,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            };
            Canvas.SetLeft(bar, i * step + (step - BarWidth) / 2);
            SetBarHeight(bar, 0);
            WaveCanvas.Children.Add(bar);
            _bars[i] = bar;
        }
    }

    private static void SetBarHeight(Rectangle bar, double level)
    {
        // sqrt lifts quiet sound so a normal voice or soft playback still visibly moves the bars.
        double height = Math.Max(3, Math.Sqrt(Math.Clamp(level, 0, 1)) * WaveHeight);
        bar.Height = height;
        Canvas.SetTop(bar, (WaveHeight - height) / 2);
    }

    private void ResetWaveform()
    {
        foreach (Rectangle bar in _bars)
        {
            SetBarHeight(bar, 0);
        }
    }

    /// <summary>Scrolls the waveform one step left and appends the newest level on the right.</summary>
    private void PushLevel(float level)
    {
        for (int i = 0; i < BarCount - 1; i++)
        {
            SetBarHeight(_bars[i], Math.Pow(_bars[i + 1].Height / WaveHeight, 2));
        }

        SetBarHeight(_bars[BarCount - 1], level);
    }

    private void RecordingTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        TimeSpan elapsed = _recordingClock.Elapsed;
        RecordingTime.Text = SoundNamePolicy.FormatDuration(elapsed);
        if (_recorder is { } live)
        {
            PushLevel(live.Level);
        }

        // Blink the dot so it reads as "live".
        RecordingDot.Opacity = elapsed.Milliseconds < 500 ? 1 : 0.35;

        if (elapsed >= AudioRecorder.MaxLength)
        {
            _ = StopRecordingAsync();
        }
    }

    private async Task StopRecordingAsync()
    {
        if (_recorder is not { } recorder)
        {
            return;
        }

        _recorder = null;
        _recordingTimer.Stop();
        _recordingClock.Stop();
        TimeSpan length = _recordingClock.Elapsed;

        try
        {
            await recorder.StopAsync();
        }
        catch (Exception ex)
        {
            AudioRecorder.TryDelete(recorder.FilePath);
            ShowError("The recording didn't save", ex.Message);
            ShowEmpty();
            return;
        }

        ClearSource();
        _sourcePath = recorder.FilePath;
        _sourceIsRecording = true;
        if (SoundNamePolicy.Clean(NameBox.Text).Length == 0)
        {
            NameBox.Text = SoundLibrary.UniqueName("My recording");
        }

        ShowReady($"Recording · {SoundNamePolicy.FormatDuration(length)}", SoundLibrary.RecordingEmoji);
        LoadWaveform(recorder.FilePath!, null);
    }

    // Files

    private async Task ChooseFileAsync()
    {
        ErrorBar.IsOpen = false;
        FileOpenPicker picker = new() { SuggestedStartLocation = PickerLocationId.MusicLibrary };
        foreach (string extension in SoundNamePolicy.SupportedExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFile? file;
        try
        {
            file = await picker.PickSingleFileAsync();
        }
        catch (Exception ex)
        {
            ShowError("Can't open the file picker", ex.Message);
            return;
        }

        if (file is not null)
        {
            UseFile(file.Path);
        }
    }

    private void UseFile(string path)
    {
        if (!SoundNamePolicy.IsSupported(path))
        {
            ShowError("That file type isn't supported", $"Use one of: {string.Join(", ", SoundNamePolicy.SupportedExtensions)}.");
            return;
        }

        try
        {
            if (new FileInfo(path).Length > SoundNamePolicy.MaxFileBytes)
            {
                ShowError("That file is too large", "Pick a clip under 50 MB.");
                return;
            }
        }
        catch (Exception ex)
        {
            ShowError("Can't read that file", ex.Message);
            return;
        }

        ErrorBar.IsOpen = false;
        StopPreview();
        ClearSource();
        _sourcePath = path;
        _sourceIsRecording = false;
        NameBox.Text = SoundLibrary.UniqueName(SoundNamePolicy.FromFileName(path));
        ShowReady(Path.GetFileName(path), SoundLibrary.FileEmoji);
        LoadWaveform(path, null);
    }

    // Waveform and trim

    private void LoadWaveform(string path, SoundTrim? trim)
    {
        CancelWaveformLoad();
        CancellationTokenSource load = _waveformLoad = new();
        ResetTrim();
        WaveformLoading.IsActive = true;
        _ = LoadAsync();

        async Task LoadAsync()
        {
            Waveform? waveform = null;
            try
            {
                waveform = await WaveformReader.ReadAsync(path, WaveBuckets, load.Token);
            }
            catch
            {
                // Cancelled, or a format Media Foundation can't decode; the sound just can't be trimmed.
            }

            if (load.IsCancellationRequested || _isClosed)
            {
                return;
            }

            WaveformLoading.IsActive = false;
            if (waveform is null || waveform.Duration <= TimeSpan.Zero)
            {
                return;
            }

            _waveform = waveform;
            SoundTrim shown = trim is { } saved
                ? SoundTrimPolicy.Clamp(saved.Start, saved.End, waveform.Duration)
                : new(TimeSpan.Zero, waveform.Duration);
            _settingTrim = true;
            TrimSelector.Minimum = 0;
            TrimSelector.Maximum = waveform.Duration.TotalMilliseconds;
            TrimSelector.RangeStart = shown.Start.TotalMilliseconds;
            TrimSelector.RangeEnd = shown.End.TotalMilliseconds;
            _settingTrim = false;
            TrimSelector.IsEnabled = true;
            DrawWaveform();
            UpdateTrimLabels();
        }
    }

    private void CancelWaveformLoad()
    {
        _waveformLoad?.Cancel();
        _waveformLoad = null;
    }

    private void ResetTrim()
    {
        _waveform = null;
        _settingTrim = true;
        TrimSelector.IsEnabled = false;
        TrimSelector.Minimum = 0;
        TrimSelector.Maximum = 1000;
        TrimSelector.RangeStart = 0;
        TrimSelector.RangeEnd = 1000;
        _settingTrim = false;
        WaveformPath.Data = null;
        WaveformSelectedPath.Data = null;
        WaveformLoading.IsActive = false;
        TrimStartText.Text = TrimEndText.Text = TrimLengthText.Text = string.Empty;
    }

    private SoundTrim SelectedRange() =>
        new(TimeSpan.FromMilliseconds(TrimSelector.RangeStart), TimeSpan.FromMilliseconds(TrimSelector.RangeEnd));

    /// <summary>The slice to store: null when it covers the whole clip. Keeps the saved trim if the clip couldn't be decoded.</summary>
    private SoundTrim? CurrentTrim() =>
        _waveform is { } waveform ? SoundTrimPolicy.ToStored(SelectedRange(), waveform.Duration) : _editing?.Trim;

    private void TrimSelector_ValueChanged(object sender, CommunityToolkit.WinUI.Controls.RangeChangedEventArgs e)
    {
        if (_settingTrim || _waveform is not { } waveform)
        {
            return;
        }

        SoundTrim range = SelectedRange();
        SoundTrim clamped = SoundTrimPolicy.Clamp(range.Start, range.End, waveform.Duration);
        if (clamped != range)
        {
            _settingTrim = true;
            TrimSelector.RangeStart = clamped.Start.TotalMilliseconds;
            TrimSelector.RangeEnd = clamped.End.TotalMilliseconds;
            _settingTrim = false;
        }

        UpdateTrimLabels();
    }

    private void UpdateTrimLabels()
    {
        if (_waveform is not { } waveform)
        {
            return;
        }

        SoundTrim range = SelectedRange();
        TrimStartText.Text = SoundTrimPolicy.FormatTime(range.Start);
        TrimEndText.Text = SoundTrimPolicy.FormatTime(range.End);
        TrimLengthText.Text = $"{SoundTrimPolicy.FormatTime(range.Length)} selected";
        double width = WaveformPath.ActualWidth;
        double total = waveform.Duration.TotalMilliseconds;
        WaveformSelectedPath.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(
                range.Start.TotalMilliseconds / total * width,
                0,
                range.Length.TotalMilliseconds / total * width,
                WaveformHeight),
        };
    }

    /// <summary>One vertical line per peak, mirrored around the middle. The selected slice is the same shape clipped and drawn in the accent color.</summary>
    private void DrawWaveform()
    {
        if (_waveform is not { } waveform)
        {
            return;
        }

        double width = WaveformHost.ActualWidth - WaveformPath.Margin.Left - WaveformPath.Margin.Right;
        if (width <= 0)
        {
            return;
        }

        // A geometry can only belong to one element, so each path gets its own copy.
        WaveformPath.Data = BuildWaveformGeometry(waveform.Peaks, width);
        WaveformSelectedPath.Data = BuildWaveformGeometry(waveform.Peaks, width);
        UpdateTrimLabels();
    }

    private static PathGeometry BuildWaveformGeometry(float[] peaks, double width)
    {
        PathGeometry geometry = new();
        double step = width / peaks.Length;
        for (int i = 0; i < peaks.Length; i++)
        {
            double half = Math.Max(1, Math.Sqrt(peaks[i]) * WaveformHeight / 2);
            double x = (i + 0.5) * step;
            PathFigure figure = new() { StartPoint = new Windows.Foundation.Point(x, WaveformHeight / 2 - half) };
            figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x, WaveformHeight / 2 + half) });
            geometry.Figures.Add(figure);
        }

        return geometry;
    }

    private void SourceCard_DragOver(object sender, DragEventArgs e)
    {
        if (_recorder is null && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add sound";
            DropHint.Text = "Drop to add";
        }
    }

    private void SourceCard_DragLeave(object sender, DragEventArgs e) => DropHint.Text = "Or drop an audio file here";

    private async void SourceCard_Drop(object sender, DragEventArgs e)
    {
        DropHint.Text = "Or drop an audio file here";
        if (_recorder is not null || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
        IStorageItem? item = items.FirstOrDefault(i => i is StorageFile && SoundNamePolicy.IsSupported(i.Path)) ?? items.FirstOrDefault();
        if (item is not null)
        {
            UseFile(item.Path);
        }
    }

    // Preview

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_preview is not null)
        {
            StopPreview();
            return;
        }

        if (_sourcePath is null)
        {
            return;
        }

        MediaPlayer player = new() { AudioCategory = MediaPlayerAudioCategory.SoundEffects, Volume = SettingsService.Volume };
        player.CommandManager.IsEnabled = false;
        player.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(StopPreview);
        player.MediaFailed += (_, args) => DispatcherQueue.TryEnqueue(() =>
        {
            StopPreview();
            ShowError("Can't play this file", "It may be damaged or in a format Windows can't play.");
        });
        _preview = player;
        PreviewIcon.Glyph = "";
        PreviewText.Text = "Stop";

        try
        {
            player.Source = PlaybackService.CreateSource(await StorageFile.GetFileFromPathAsync(_sourcePath), CurrentTrim());
            player.Play();
        }
        catch (Exception ex)
        {
            StopPreview();
            ShowError("Can't play this file", ex.Message);
        }
    }

    private void StopPreview()
    {
        if (_preview is { } player)
        {
            _preview = null;
            player.Pause();
            player.Source = null;
            player.Dispose();
        }

        PreviewIcon.Glyph = "";
        PreviewText.Text = "Preview";
    }

    // Commands

    private void RecordButton_Click(object sender, RoutedEventArgs e) => _ = StartRecordingAsync();

    private void StopRecordingButton_Click(object sender, RoutedEventArgs e) => _ = StopRecordingAsync();

    private void ChooseButton_Click(object sender, RoutedEventArgs e) => _ = ChooseFileAsync();

    private void StartOver_Click(object sender, RoutedEventArgs e)
    {
        StopPreview();
        ClearSource();
        ResetTrim();
        NameBox.Text = string.Empty;
        _emojiChosen = false;
        SetEmoji(SoundLibrary.FileEmoji);
        ShowEmpty();
        RecordButton.Focus(FocusState.Programmatic);
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateAddButton();

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sourcePath is null)
        {
            return;
        }

        StopPreview();
        _isBusy = true;
        UpdateAddButton();
        try
        {
            if (_editing is not null)
            {
                PlaybackService.Stop(_editing.Id); // so the next play uses the new slice
                SoundLibrary.Update(_editing.Id, NameBox.Text, _emoji, CurrentTrim());
            }
            else
            {
                await SoundLibrary.AddAsync(NameBox.Text, _sourcePath, _sourceIsRecording, _emoji, CurrentTrim());
            }
        }
        catch (Exception ex)
        {
            _isBusy = false;
            UpdateAddButton();
            ShowError("Couldn't add the sound", ex.Message);
            return;
        }

        _sourcePath = null; // now owned by the library (and in edit mode it always was)
        Close();
        App.Current.ShowFlyout();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Escape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    /// <summary>Forgets the current source, deleting it if it was a temp recording.</summary>
    private void ClearSource()
    {
        CancelWaveformLoad();
        if (_sourceIsRecording)
        {
            AudioRecorder.TryDelete(_sourcePath);
        }

        _sourcePath = null;
        _sourceIsRecording = false;
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _isClosed = true;
        _recordingTimer.Stop();
        StopPreview();
        if (_recorder is { } recorder)
        {
            _recorder = null;
            await recorder.DisposeAsync();
            AudioRecorder.TryDelete(recorder.FilePath);
        }

        ClearSource();
    }
}
