using Discord;
using Discord.Rest;

namespace ClipShare.PiServer.Services;

public sealed class DiscordNotifier
{
    public async Task PostLinkAsync(string botToken, ulong channelId, string message)
    {
        using var client = new DiscordRestClient();
        await client.LoginAsync(TokenType.Bot, botToken);

        var channel = await client.GetChannelAsync(channelId) as IRestMessageChannel
            ?? throw new InvalidOperationException($"Channel {channelId} not found or isn't a text channel the bot can post in.");

        await channel.SendMessageAsync(message);
    }
}
