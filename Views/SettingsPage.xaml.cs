using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraySounds.Services;
using Windows.ApplicationModel;

namespace TraySounds.Views;

/// <summary>Settings shown inside the flyout; there are only a few, so no separate window.</summary>
public sealed partial class SettingsPage : Page
{
    private readonly Action _goBack;
    private bool _loading = true;

    public SettingsPage(Action goBack)
    {
        _goBack = goBack;
        InitializeComponent();
        VersionText.Text = $"{App.DisplayName} {GetVersion()}";
    }

    /// <summary>Re-reads state each time; startup can change in Windows Settings meanwhile.</summary>
    public void OnShown()
    {
        _loading = true;
        OneAtATimeToggle.IsOn = SettingsService.OneSoundAtATime;
        GridSizeSlider.Value = SettingsService.GridColumns;
        ShowGridSize();
        _loading = false;
        _ = LoadStartupStateAsync();
        BackButton.Focus(FocusState.Programmatic);
    }

    private static string GetVersion()
    {
        try
        {
            PackageVersion v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return "(unpackaged)";
        }
    }

    private async Task LoadStartupStateAsync() => ShowStartupState(await StartupService.GetStateAsync());

    private void ShowStartupState(StartupTaskState? state)
    {
        _loading = true;
        StartupToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

        // The user (Task Manager, Settings > Apps > Startup) or policy has the final say; the app
        // can't override it, so say where to change it instead of offering a dead toggle.
        StartupToggle.IsEnabled = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupDescription.Text = state switch
        {
            StartupTaskState.DisabledByUser => "Turned off in Settings > Apps > Startup. Turn it on there.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Managed by your organization.",
            null => "Only available when the app is installed.",
            _ => "Keep the soundboard ready after you sign in.",
        };
        _loading = false;
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        ShowStartupState(await StartupService.SetEnabledAsync(StartupToggle.IsOn));
    }

    private void OneAtATimeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            SettingsService.OneSoundAtATime = OneAtATimeToggle.IsOn;
        }
    }

    private void GridSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_loading)
        {
            SettingsService.GridColumns = (int)GridSizeSlider.Value;
            ShowGridSize();
        }
    }

    private void ShowGridSize() =>
        GridSizeDescription.Text = $"{(int)GridSizeSlider.Value} sounds per row. Drag tiles on the board to rearrange them.";

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SoundLibrary.SoundsFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SoundLibrary.SoundsFolder}\"") { UseShellExecute = true });
        }
        catch
        {
            // Explorer unavailable; nothing useful to do.
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => _goBack();
}
