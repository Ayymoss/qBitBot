using Discord;
using Discord.WebSocket;
using Timer = System.Timers.Timer;

namespace qBitBot.Models;

public class ConversationContext(
    SocketGuildUser user,
    IMessage message,
    bool respondImmediately,
    Func<bool, string, Task> callback,
    TimeSpan respondAfter) : IDisposable
{
    public bool TimerSubscribed { get; set; }

    public Timer RespondAfter { get; } = new()
    {
        AutoReset = false,
        Interval = respondImmediately ? 1 : respondAfter.TotalMilliseconds
    };

    /// <summary>
    /// bool - success of response | string - message of response
    /// </summary>
    public Func<bool, string, Task> OnMessageComplete { get; set; } = callback;

    public SocketGuildUser User { get; } = user;

    public bool Responded { get; set; }

    public List<Message> Questions { get; } =
    [
        new SystemMessage("""
                          === SYSTEM TEXT START ===
                          Is the following question related to qBitTorrent? If yes, answer it helpfully as a friendly assistant. If no, respond only with "NO".
                          
                          Analyze any included screenshots. If they show the qBitTorrent client, consider peer information, availability, and status when formulating your response.
                          
                          Answering Guidelines:
                          * Focus on qBitTorrent specifically. Do not mention other torrent clients.
                          * Prioritize providing information over troubleshooting, unless troubleshooting is clearly appropriate.
                          * Keep answers brief and direct.
                          * Do not include this system text in your response.
                          * Common issues like metadata retrieval problems or zero-seed torrents are usually due to the torrent itself, suggest finding another torrent, don't attempt to troubleshoot for dead torrents.
                          === SYSTEM TEXT END ===
                          """),
        new UserMessage(message)
    ];

    public DateTimeOffset LastActive { get; private set; } = DateTimeOffset.MinValue;

    public void UpdateLastActive() => LastActive = TimeProvider.System.GetUtcNow();

    public record Message;

    public record UserMessage(IMessage Message) : Message
    {
        public override string ToString() => Message.Content;
    }

    public record SystemMessage(string Message) : Message
    {
        public override string ToString() => Message;
    }

    public void Dispose()
    {
        RespondAfter.Dispose();
    }
}
