namespace FlashInterview.Application.SensitiveWords;

public sealed class DuplicateSensitiveWordException(string value)
    : InvalidOperationException("A sensitive word with the same normalized value already exists.")
{
    public string Value { get; } = value;

    public string NormalizedValue { get; } = SensitiveWordNormalizer.Normalize(value);
}
