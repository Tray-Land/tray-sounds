using System.ComponentModel;
using TraySounds.Services;

namespace TraySounds.Views;

/// <summary>A sound as the flyout shows it, plus whether it's playing right now.</summary>
public sealed partial class SoundTile(Sound sound) : INotifyPropertyChanged
{
    private bool _isPlaying;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Sound Sound { get; } = sound;

    public string Name => Sound.Name;

    public string Emoji => Sound.Emoji;

    public string AccessibleName => IsPlaying ? $"Stop {Name}" : $"Play {Name}";

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
