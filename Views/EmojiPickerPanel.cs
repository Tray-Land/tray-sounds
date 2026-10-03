using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraySounds.Services;

namespace TraySounds.Views;

/// <summary>
/// Flyout content for choosing a sound's emoji: a grid of suggestions, plus a box for typing or
/// pasting any other emoji (Win + . opens Windows' own emoji panel).
/// </summary>
public sealed class EmojiPickerPanel : StackPanel
{
    private const int Columns = 8;

    public EmojiPickerPanel()
    {
        Spacing = 8;
        Width = 8 * 40 + 7 * 2;

        Grid grid = new() { ColumnSpacing = 2, RowSpacing = 2 };
        for (int c = 0; c < Columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        IReadOnlyList<string> choices = SoundNamePolicy.EmojiChoices;
        for (int i = 0; i < choices.Count; i++)
        {
            if (i % Columns == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            string emoji = choices[i];
            Button button = new()
            {
                Content = new TextBlock { Text = emoji, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Emoji"), FontSize = 20 },
                Height = 40,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, emoji);
            button.Click += (_, _) => EmojiPicked?.Invoke(this, emoji);
            Grid.SetRow(button, i / Columns);
            Grid.SetColumn(button, i % Columns);
            grid.Children.Add(button);
        }

        TextBox custom = new()
        {
            Header = "Or type any emoji (Win + .)",
            PlaceholderText = "😀",
            MaxLength = 16,
        };
        custom.TextChanged += (_, _) =>
        {
            string emoji = SoundNamePolicy.FirstEmoji(custom.Text, string.Empty);
            if (emoji.Length > 0)
            {
                custom.Text = string.Empty;
                EmojiPicked?.Invoke(this, emoji);
            }
        };

        Children.Add(grid);
        Children.Add(custom);
    }

    public event EventHandler<string>? EmojiPicked;
}
