using GenerativeAI.Classes;

namespace qBitBot.Models;

public class PromptImage : PromptContentBase
{
    public required FileObject Image { get; set; }
}
