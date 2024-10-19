using Discord.WebSocket;

namespace qBitBot.Models;

public class ConversationContext(SocketGuildUser user, SocketUserMessage message, bool respondImmediately)
{
    public SocketGuildUser User { get; } = user;
    public List<Question> Questions { get; } = [new UserQuestion(message, respondImmediately)];
    public DateTimeOffset LastActive { get; private set; } = DateTimeOffset.MinValue;

    public bool UsageCapHit => Questions.Count > 3;
    public bool UsageCapInformed { get; set; }

    public void UpdateLastActive() => LastActive = TimeProvider.System.GetUtcNow();

    public record Question;

    public record UserQuestion(SocketUserMessage Message, bool RespondImmediately) : Question
    {
        public bool Responded { get; set; }
        public override string ToString() => Message.Content;
    }

    public record SystemQuestion(string Message) : Question
    {
        public override string ToString() => Message;
    }
}
