namespace qBitBot.Utilities;

public class Configuration
{
    public string GeminiToken { get; set; } = null!;
    public string BotToken { get; set; } = null!;
    public bool Debug { get; set; }
    public TimeSpan GeminiRespondAfter { get; set; } = TimeSpan.FromMinutes(20);
    public TimeSpan IgnoreUserAfter { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan DeleteQuestionsAfter { get; set; } = TimeSpan.FromDays(1);
}
