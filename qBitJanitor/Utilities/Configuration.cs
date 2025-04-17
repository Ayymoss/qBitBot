namespace qBitJanitor.Utilities;

public class Configuration
{
    public string GeminiToken { get; set; } = null!;
    public string BotToken { get; set; } = null!;
    public TimeSpan IgnoreUserAfter { get; set; } = TimeSpan.FromHours(3);
    public bool Debug { get; set; }
}
