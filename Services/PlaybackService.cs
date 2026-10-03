using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace TraySounds.Services;

/// <summary>
/// Plays sounds independently of any window, so a clip keeps going after the flyout light-dismisses.
/// Each playing sound gets its own <see cref="MediaPlayer"/>, disposed as soon as it ends, so an idle
/// app holds no players at all.
/// </summary>
internal static class PlaybackService
{
    private static readonly Dictionary<string, MediaPlayer> s_playing = [];
    private static DispatcherQueue? s_dispatcher;

    /// <summary>Raised on the UI thread whenever a sound starts or stops.</summary>
    public static event EventHandler? Changed;

    /// <summary>Raised on the UI thread when a file can't be played.</summary>
    public static event EventHandler<Sound>? Failed;

    public static void Initialize(DispatcherQueue dispatcher) => s_dispatcher = dispatcher;

    public static bool IsAnyPlaying => s_playing.Count > 0;

    public static bool IsPlaying(string id) => s_playing.ContainsKey(id);

    /// <summary>Names of what's playing, for the tray tooltip.</summary>
    public static IEnumerable<string> PlayingNames() =>
        s_playing.Keys.Select(id => SoundLibrary.Find(id)?.Name).OfType<string>();

    /// <summary>Tile-click behavior: start the sound, or stop it if it's already playing.</summary>
    public static Task ToggleAsync(Sound sound)
    {
        if (IsPlaying(sound.Id))
        {
            Stop(sound.Id);
            return Task.CompletedTask;
        }

        return PlayAsync(sound);
    }

    public static async Task PlayAsync(Sound sound)
    {
        if (SettingsService.OneSoundAtATime)
        {
            StopAll();
        }
        else
        {
            Stop(sound.Id); // restart rather than stack the same clip
        }

        MediaPlayer player = new()
        {
            AudioCategory = MediaPlayerAudioCategory.SoundEffects,
            Volume = SettingsService.Volume,
            AutoPlay = false,
        };

        // Sound effects don't belong in the media flyout or behind the keyboard's media keys.
        player.CommandManager.IsEnabled = false;
        player.MediaEnded += (_, _) => s_dispatcher?.TryEnqueue(() => Finish(sound.Id, player));
        player.MediaFailed += (_, _) => s_dispatcher?.TryEnqueue(() =>
        {
            Finish(sound.Id, player);
            Failed?.Invoke(null, sound);
        });

        s_playing[sound.Id] = player;
        Changed?.Invoke(null, EventArgs.Empty);

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(sound.FilePath);
            if (!s_playing.TryGetValue(sound.Id, out MediaPlayer? current) || current != player)
            {
                player.Dispose(); // stopped while the file was opening
                return;
            }

            player.Source = MediaSource.CreateFromStorageFile(file);
            player.Play();
        }
        catch
        {
            Finish(sound.Id, player);
            Failed?.Invoke(null, sound);
        }
    }

    public static void Stop(string id)
    {
        if (s_playing.Remove(id, out MediaPlayer? player))
        {
            Dispose(player);
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static void StopAll()
    {
        if (s_playing.Count == 0)
        {
            return;
        }

        foreach (MediaPlayer player in s_playing.Values)
        {
            Dispose(player);
        }

        s_playing.Clear();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Applies a volume change to what's already playing, too.</summary>
    public static void SetVolume(double volume)
    {
        SettingsService.Volume = volume;
        foreach (MediaPlayer player in s_playing.Values)
        {
            player.Volume = volume;
        }
    }

    private static void Finish(string id, MediaPlayer player)
    {
        // Only remove the entry if it's still this player; a restart may have replaced it.
        if (s_playing.TryGetValue(id, out MediaPlayer? current) && current == player)
        {
            s_playing.Remove(id);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        Dispose(player);
    }

    private static void Dispose(MediaPlayer player)
    {
        try
        {
            player.Pause();
            player.Source = null;
            player.Dispose();
        }
        catch
        {
            // Already disposed.
        }
    }
}
