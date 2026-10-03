using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using TraySounds.Services;

namespace TraySounds.Views;

/// <summary>
/// The soundboard: the user's sounds above the stock ones. Playback lives in
/// <see cref="PlaybackService"/>, so sounds keep playing after the flyout hides; this page only
/// listens for changes while it's on screen.
/// </summary>
public sealed partial class FlyoutPage : Page, IDisposable
{
    private readonly ObservableCollection<SoundTile> _userTiles = [];
    private readonly List<SoundTile> _stockTiles;
    private bool _isShown;
    private bool _loadingVolume;

    public FlyoutPage()
    {
        InitializeComponent();
        TitleText.Text = App.DisplayName;
        _stockTiles = SoundLibrary.Stock().Select(s => new SoundTile(s)).ToList();
        StockRepeater.ItemsSource = _stockTiles;
        UserRepeater.ItemsSource = _userTiles;
    }

    /// <summary>The flyout opened: pick up anything that changed while it was hidden.</summary>
    public void OnShown()
    {
        if (!_isShown)
        {
            _isShown = true;
            PlaybackService.Changed += OnPlaybackChanged;
            PlaybackService.Failed += OnPlaybackFailed;
            SoundLibrary.Changed += OnLibraryChanged;
        }

        ReloadUserSounds();
        SyncPlaying();
        BoardScroller.ChangeView(null, 0, null, disableAnimation: true);

        _loadingVolume = true;
        VolumeSlider.Value = Math.Round(SettingsService.Volume * 100);
        _loadingVolume = false;
        UpdateVolumeIcon();
    }

    /// <summary>The flyout hid: stop listening (sounds keep playing).</summary>
    public void OnHidden()
    {
        if (!_isShown)
        {
            return;
        }

        _isShown = false;
        PlaybackService.Changed -= OnPlaybackChanged;
        PlaybackService.Failed -= OnPlaybackFailed;
        SoundLibrary.Changed -= OnLibraryChanged;
        StatusBar.IsOpen = false;
    }

    public void Dispose() => OnHidden();

    private void ReloadUserSounds()
    {
        IReadOnlyList<Sound> sounds = SoundLibrary.UserSounds();
        _userTiles.Clear();
        foreach (Sound sound in sounds)
        {
            _userTiles.Add(new SoundTile(sound));
        }

        EmptyCard.Visibility = sounds.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UserRepeater.Visibility = sounds.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SyncPlaying();
    }

    private void SyncPlaying()
    {
        foreach (SoundTile tile in _userTiles.Concat(_stockTiles))
        {
            tile.IsPlaying = PlaybackService.IsPlaying(tile.Sound.Id);
        }

        StopButton.IsEnabled = PlaybackService.IsAnyPlaying;
    }

    private void OnPlaybackChanged(object? sender, EventArgs e) => SyncPlaying();

    private void OnLibraryChanged(object? sender, EventArgs e) => ReloadUserSounds();

    private void OnPlaybackFailed(object? sender, Sound sound)
    {
        StatusBar.Title = $"Can't play {sound.Name}";
        StatusBar.Message = sound.IsStock
            ? "The sound file is missing. Reinstalling the app will restore it."
            : "The file may be damaged or in a format Windows can't play.";
        StatusBar.IsOpen = true;
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SoundTile tile })
        {
            StatusBar.IsOpen = false;
            _ = PlaybackService.ToggleAsync(tile.Sound);
        }
    }

    private void Tile_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: SoundTile tile } element)
        {
            return;
        }

        args.Handled = true;
        MenuFlyout menu = new();
        MenuFlyoutItem play = new()
        {
            Text = tile.IsPlaying ? "Stop" : "Play",
            Icon = new FontIcon { Glyph = tile.IsPlaying ? "" : "" },
        };
        play.Click += (_, _) => _ = PlaybackService.ToggleAsync(tile.Sound);
        menu.Items.Add(play);

        if (!tile.Sound.IsStock)
        {
            MenuFlyoutItem rename = new() { Text = "Rename", Icon = new FontIcon { Glyph = "" } };
            rename.Click += (_, _) => ShowRename(element, tile.Sound);
            MenuFlyoutItem reveal = new() { Text = "Show in File Explorer", Icon = new FontIcon { Glyph = "" } };
            reveal.Click += (_, _) => RevealInExplorer(tile.Sound.FilePath);
            MenuFlyoutItem delete = new() { Text = "Delete", Icon = new FontIcon { Glyph = "" } };
            delete.Click += (_, _) => ConfirmDelete(element, tile.Sound);

            menu.Items.Add(rename);
            menu.Items.Add(reveal);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(delete);
        }

        if (args.TryGetPosition(element, out Windows.Foundation.Point point))
        {
            menu.ShowAt(element, new FlyoutShowOptions { Position = point });
        }
        else
        {
            menu.ShowAt(element);
        }
    }

    private static void ShowRename(FrameworkElement anchor, Sound sound)
    {
        TextBox box = new()
        {
            Text = sound.Name,
            MaxLength = SoundNamePolicy.MaxNameLength,
            Width = 220,
            Header = "Name",
        };
        Button save = new() { Content = "Rename", Style = (Style)Application.Current.Resources["AccentButtonStyle"], HorizontalAlignment = HorizontalAlignment.Right };
        StackPanel panel = new() { Spacing = 12 };
        panel.Children.Add(box);
        panel.Children.Add(save);
        Flyout flyout = new() { Content = panel };

        void Commit()
        {
            SoundLibrary.Rename(sound.Id, box.Text);
            flyout.Hide();
        }

        save.Click += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                Commit();
            }
        };
        flyout.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        flyout.ShowAt(anchor);
    }

    private static void ConfirmDelete(FrameworkElement anchor, Sound sound)
    {
        Button delete = new() { Content = "Delete", Style = (Style)Application.Current.Resources["AccentButtonStyle"], HorizontalAlignment = HorizontalAlignment.Right };
        StackPanel panel = new() { Spacing = 12, MaxWidth = 240 };
        panel.Children.Add(new TextBlock { Text = $"Delete {sound.Name}? This removes the app's copy of the sound.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(delete);
        Flyout flyout = new() { Content = panel };
        delete.Click += (_, _) =>
        {
            PlaybackService.Stop(sound.Id);
            SoundLibrary.Remove(sound.Id);
            flyout.Hide();
        };
        flyout.ShowAt(anchor);
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // Explorer unavailable; nothing useful to do.
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loadingVolume)
        {
            return;
        }

        PlaybackService.SetVolume(e.NewValue / 100);
        UpdateVolumeIcon();
    }

    private void UpdateVolumeIcon()
    {
        double v = VolumeSlider.Value;
        VolumeIcon.Glyph = v <= 0 ? "" : v < 34 ? "" : v < 67 ? "" : "";
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => PlaybackService.StopAll();

    private void Record_Click(object sender, RoutedEventArgs e) => App.Current.ShowAddSound(AddSoundStart.Record);

    private void ChooseFile_Click(object sender, RoutedEventArgs e) => App.Current.ShowAddSound(AddSoundStart.ChooseFile);

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();
}
