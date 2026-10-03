using System.ComponentModel;
using TraySounds.Services;

namespace TraySounds.Views;

/// <summary>A sound as the flyout shows it, plus whether it's playing right now.</summary>
public sealed partial class SoundTile(Sound sound) : INotifyPropertyChanged
{
    private bool _isPlaying;
    private string _hotkey = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public Sound Sound { get; } = sound;

    public string Name => Sound.Name;

    public string Emoji => Sound.Emoji;

    public string ToolTip => Sound.Credit is { } credit ? $"{Name}\n{credit.Summary}" : Name;

    public string AccessibleName => IsPlaying ? $"Stop {Name}" : $"Play {Name}";

    /// <summary>The key that triggers this tile ("1"–"9", "0" for the tenth), or empty past that.</summary>
    public string Hotkey
    {
        get => _hotkey;
        set
        {
            if (_hotkey == value)
            {
                return;
            }

            _hotkey = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hotkey)));
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
        }
    }
}
