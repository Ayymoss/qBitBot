using Discord.WebSocket;

namespace qBitBot.Models;

public class UserContext
{
    public required SocketUser SocketUser { get; set; }
    public required SocketUserMessage SocketUserMessage { get; set; }
    public required DateTimeOffset Created { get; set; }
}
