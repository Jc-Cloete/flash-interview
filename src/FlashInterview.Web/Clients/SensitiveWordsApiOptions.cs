namespace FlashInterview.Web.Clients;

public sealed class SensitiveWordsApiOptions
{
    public const string SectionName = "SensitiveWordsApi";

    public required string BaseUrl { get; init; }

    public string? AdminApiKey { get; init; }
}
