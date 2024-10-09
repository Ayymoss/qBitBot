using Discord.WebSocket;

namespace qBitBot.Models;

public class UserContext
{
    public required string UserName { get; set; }
    public required ulong UserId { get; set; }
    public required SocketUserMessage SocketUserMessage { get; set; }
    public required DateTimeOffset Created { get; set; }
}
