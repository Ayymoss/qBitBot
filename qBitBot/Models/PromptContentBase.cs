using GenerativeAI.Classes;
using qBitBot.Enums;

namespace qBitBot.Models;

public abstract class PromptContentBase
{
    public required SenderType Sender { get; set; }
    public required ulong MessageId { get; set; }

    public class PromptImage : PromptContentBase
    {
        public required FileObject Image { get; set; }
    }

    public class PromptText : PromptContentBase
    {
        public required string Text { get; set; }
    }
}
