namespace qBitBot.Utilities;

public class Configuration
{
    public string GeminiToken { get; set; } = null!;
    public string BotToken { get; set; } = null!;
    public bool Debug { get; set; }
    public TimeSpan GeminiRespondAfter { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan IgnoreUserAfter { get; set; } = TimeSpan.FromHours(3);
    public TimeSpan DeleteQuestionsAfter { get; set; } = TimeSpan.FromDays(1);
}
