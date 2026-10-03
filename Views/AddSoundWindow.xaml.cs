using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

    public AddSoundWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        WindowPlacementService.CenterOnPrimary(this, 440, 420);
        IsMaximizable = false;
        IsMinimizable = false;
        IsResizable = false;

        RecordingLimit.Text = $"Recordings stop on their own after {SoundNamePolicy.FormatDuration(AudioRecorder.MaxLength)}.";
        _recordingTimer = DispatcherQueue.CreateTimer();
        _recordingTimer.Interval = TimeSpan.FromMilliseconds(250);
        _recordingTimer.Tick += RecordingTimer_Tick;

        Closed += OnClosed;
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
        ReadyEmoji.Text = emoji;
        ReadyDescription.Text = description;
        NameBox.Focus(FocusState.Programmatic);
        NameBox.SelectAll();
        UpdateAddButton();
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
        PlaybackService.StopAll(); // don't record the soundboard itself
        _isBusy = true;
        RecordButton.IsEnabled = false;

        AudioRecorder recorder = new();
        try
        {
            await recorder.StartAsync();
        }
        catch (Exception ex)
        {
            _isBusy = false;
            RecordButton.IsEnabled = true;
            if (ex is UnauthorizedAccessException || ex.HResult == MicrophoneDisabledHResult)
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
        _recordingTimer.Start();
        ShowRecording();
    }

    private void RecordingTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        TimeSpan elapsed = _recordingClock.Elapsed;
        RecordingTime.Text = SoundNamePolicy.FormatDuration(elapsed);

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
            player.Source = MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(_sourcePath));
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
        NameBox.Text = string.Empty;
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
            await SoundLibrary.AddAsync(NameBox.Text, _sourcePath, _sourceIsRecording);
        }
        catch (Exception ex)
        {
            _isBusy = false;
            UpdateAddButton();
            ShowError("Couldn't add the sound", ex.Message);
            return;
        }

        _sourcePath = null; // now owned by the library
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
