namespace Bagly.Api.Options;

public class OpenAiOptions
{
    public const string SectionName = "OpenAi";

    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public int TimeoutSeconds { get; set; } = 30;

    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("SET_VIA_ENV", StringComparison.OrdinalIgnoreCase);

    /// <summary>When false, the chat agent falls back to rule-based responses.</summary>
    public bool IsConfigured => !IsPlaceholder(ApiKey);
}
