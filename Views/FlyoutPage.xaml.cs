using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using TraySounds.Services;

namespace TraySounds.Views;

/// <summary>
/// The soundboard: stock and user sounds together in one grid. Playback lives in
/// <see cref="PlaybackService"/>, so sounds keep playing after the flyout hides; this page only
/// listens for changes while it's on screen.
/// </summary>
public sealed partial class FlyoutPage : Page, IDisposable
{
    private readonly ObservableCollection<SoundTile> _tiles = [];
    private bool _isShown;
    private bool _loadingVolume;

    public FlyoutPage()
    {
        InitializeComponent();
        TitleText.Text = App.DisplayName;
        TileGrid.ItemsSource = _tiles;

        // 1–9 play the first nine tiles in board order, 0 the tenth; the number pad works too.
        for (int i = 0; i < 10; i++)
        {
            int index = i;
            int digit = (i + 1) % 10;
            foreach (Windows.System.VirtualKey key in new[]
            {
                Windows.System.VirtualKey.Number0 + digit,
                Windows.System.VirtualKey.NumberPad0 + digit,
            })
            {
                KeyboardAccelerator accelerator = new() { Key = key };
                accelerator.Invoked += (_, e) =>
                {
                    e.Handled = true;
                    PlayTileAt(index);
                };
                KeyboardAccelerators.Add(accelerator);
            }
        }
    }

    private void PlayTileAt(int index)
    {
        if (index < _tiles.Count)
        {
            StatusBar.IsOpen = false;
            _ = PlaybackService.ToggleAsync(_tiles[index].Sound);
        }
    }

    private void NumberTiles()
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            _tiles[i].Hotkey = i < 10 ? ((i + 1) % 10).ToString() : "";
        }
    }

    /// <summary>Sizes tiles so exactly <see cref="SettingsService.GridColumns"/> fit across the board.</summary>
    private void UpdateTileSize()
    {
        if (TileGrid.ItemsPanelRoot is not ItemsWrapGrid panel)
        {
            return;
        }

        double available = TileGrid.ActualWidth - TileGrid.Padding.Left - TileGrid.Padding.Right;
        if (available <= 0)
        {
            return;
        }

        // Floor so rounding never pushes the last column onto a new row.
        double width = Math.Floor(available / SettingsService.GridColumns);
        panel.ItemWidth = width;
        panel.ItemHeight = Math.Round(width * 0.8);
    }

    private void TileGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTileSize();

    private void TileGrid_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        SettingsService.SoundOrder = _tiles.Select(t => t.Sound.Id).ToList();
        NumberTiles();
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

        ReloadSounds();
        SyncPlaying();
        UpdateTileSize();
        if (_tiles.Count > 0)
        {
            TileGrid.ScrollIntoView(_tiles[0]);
        }

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

    private void ReloadSounds()
    {
        _tiles.Clear();
        IEnumerable<Sound> ordered = SoundOrderPolicy.Apply(
            SoundLibrary.Stock().Concat(SoundLibrary.UserSounds()),
            SettingsService.SoundOrder,
            s => s.Id);
        foreach (Sound sound in ordered)
        {
            _tiles.Add(new SoundTile(sound));
        }

        NumberTiles();
        SyncPlaying();
    }

    private void SyncPlaying()
    {
        foreach (SoundTile tile in _tiles)
        {
            tile.IsPlaying = PlaybackService.IsPlaying(tile.Sound.Id);
        }

        StopButton.IsEnabled = PlaybackService.IsAnyPlaying;
    }

    private void OnPlaybackChanged(object? sender, EventArgs e) => SyncPlaying();

    private void OnLibraryChanged(object? sender, EventArgs e) => ReloadSounds();

    private void OnPlaybackFailed(object? sender, Sound sound)
    {
        StatusBar.Title = $"Can't play {sound.Name}";
        StatusBar.Message = sound.IsStock
            ? "The sound file is missing. Reinstalling the app will restore it."
            : "The file may be damaged or in a format Windows can't play.";
        StatusBar.IsOpen = true;
    }

    private void TileGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SoundTile tile)
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

        if (tile.Sound.Credit is { } credit)
        {
            MenuFlyoutItem author = new() { Text = $"Visit {credit.Author}'s page", Icon = new FontIcon { Glyph = "" } };
            author.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(credit.AuthorUrl);
            MenuFlyoutItem source = new() { Text = $"View on {credit.SourceName}", Icon = new FontIcon { Glyph = "" } };
            source.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(credit.SourceUrl);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(author);
            menu.Items.Add(source);
        }

        if (!tile.Sound.IsStock)
        {
            MenuFlyoutItem edit = new() { Text = "Edit…", Icon = new FontIcon { Glyph = "" } };
            edit.Click += (_, _) => App.Current.ShowEditSound(tile.Sound);
            menu.Items.Add(edit);

            MenuFlyoutItem rename = new() { Text = "Rename", Icon = new FontIcon { Glyph = "" } };
            rename.Click += (_, _) => ShowRename(element, tile.Sound);
            MenuFlyoutItem emoji = new() { Text = "Change emoji", Icon = new FontIcon { Glyph = "" } };
            emoji.Click += (_, _) => ShowEmojiPicker(element, tile.Sound);
            MenuFlyoutItem reveal = new() { Text = "Show in File Explorer", Icon = new FontIcon { Glyph = "" } };
            reveal.Click += (_, _) => RevealInExplorer(tile.Sound.FilePath);
            MenuFlyoutItem delete = new() { Text = "Delete", Icon = new FontIcon { Glyph = "" } };
            delete.Click += (_, _) => ConfirmDelete(element, tile.Sound);

            menu.Items.Add(rename);
            menu.Items.Add(emoji);
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

    private static void ShowEmojiPicker(FrameworkElement anchor, Sound sound)
    {
        EmojiPickerPanel picker = new();
        Flyout flyout = new() { Content = picker };
        picker.EmojiPicked += (_, emoji) =>
        {
            SoundLibrary.SetEmoji(sound.Id, emoji);
            flyout.Hide();
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

    private void AddSound_Click(object sender, RoutedEventArgs e) => App.Current.ShowAddSound();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();
}
