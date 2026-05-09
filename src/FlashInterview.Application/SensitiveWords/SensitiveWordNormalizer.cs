namespace FlashInterview.Application.SensitiveWords;

public static class SensitiveWordNormalizer
{
    public static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }
}
