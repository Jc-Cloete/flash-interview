using System.Text.RegularExpressions;

namespace FlashInterview.Application.SensitiveWords;

public sealed class CompiledSensitiveWordMasker
{
    private readonly PreparedSensitiveWord[] preparedWords;

    private CompiledSensitiveWordMasker(PreparedSensitiveWord[] preparedWords)
    {
        this.preparedWords = preparedWords;
    }

    public static CompiledSensitiveWordMasker FromCandidates(IEnumerable<SensitiveWordCandidate> sensitiveWords)
    {
        var preparedWords = sensitiveWords
            .Select(word => word.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(value => value.Length)
            .Select(value => new PreparedSensitiveWord(
                value,
                new Regex(
                    BuildPattern(value),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)))
            .ToArray();

        return new CompiledSensitiveWordMasker(preparedWords);
    }

    public MaskMessageResult Mask(string message)
    {
        if (message.Length == 0)
        {
            return new MaskMessageResult(message, message, []);
        }

        var reserved = new bool[message.Length];
        var matches = new List<SensitiveWordMatch>();

        foreach (var preparedWord in preparedWords)
        {
            foreach (Match match in preparedWord.Regex.Matches(message))
            {
                if (IsRangeReserved(reserved, match.Index, match.Length))
                {
                    continue;
                }

                MarkRangeReserved(reserved, match.Index, match.Length);
                matches.Add(new SensitiveWordMatch(preparedWord.Value, match.Index, match.Index + match.Length));
            }
        }

        if (matches.Count == 0)
        {
            return new MaskMessageResult(message, message, []);
        }

        var masked = message.ToCharArray();
        foreach (var match in matches)
        {
            for (var index = match.Start; index < match.End; index++)
            {
                masked[index] = '*';
            }
        }

        var orderedMatches = matches
            .OrderBy(match => match.Start)
            .ToArray();

        return new MaskMessageResult(message, new string(masked), orderedMatches);
    }

    internal static string BuildPattern(string candidate)
    {
        var escaped = Regex.Escape(candidate);
        return IsSingleWord(candidate)
            ? $@"(?<![A-Za-z0-9_]){escaped}(?![A-Za-z0-9_])"
            : escaped;
    }

    private static bool IsSingleWord(string candidate)
    {
        return candidate.All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static bool IsRangeReserved(bool[] reserved, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            if (reserved[index])
            {
                return true;
            }
        }

        return false;
    }

    private static void MarkRangeReserved(bool[] reserved, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            reserved[index] = true;
        }
    }

    private sealed record PreparedSensitiveWord(string Value, Regex Regex);
}
