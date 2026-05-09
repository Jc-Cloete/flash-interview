namespace FlashInterview.Application.SensitiveWords;

public static class SensitiveWordSeedParser
{
    public static IEnumerable<string> Parse(string source)
    {
        return source
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Trim().TrimStart(',').Trim().Trim('"'))
            .Where(line => line.Length > 0 && line is not "[" and not "]");
    }
}
