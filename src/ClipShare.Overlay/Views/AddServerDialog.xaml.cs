using System.Windows;
using System.Windows.Input;
using ClipShare.Overlay.Services;

namespace ClipShare.Overlay.Views;

/// <summary>
/// Modal prompt for a Name + Channel ID pair. The channel ID is a Discord snowflake
/// (numeric only) for a channel in a server the bot's already been invited to — this
/// dialog doesn't talk to Discord itself, it just records the pair locally so the
/// upload popup's server dropdown has something to post that clip's link to.
/// </summary>
public partial class AddServerDialog : Window
{
    public DiscordServerEntry? Result { get; private set; }

    public AddServerDialog()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NameTextBox.Focus();
        Keyboard.Focus(NameTextBox);
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var channelId = ChannelIdTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(channelId))
        {
            MessageBox.Show(this, "Both fields are required.", "ClipShare", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ulong.TryParse(channelId, out _))
        {
            MessageBox.Show(this, "Channel ID should be numbers only — copy it from Discord with Developer Mode enabled (right-click the channel → Copy Channel ID).", "ClipShare", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new DiscordServerEntry { Name = name, ChannelId = channelId };
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
