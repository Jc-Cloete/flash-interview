using System.Text.RegularExpressions;

namespace FlashInterview.Application.SensitiveWords;

public sealed class SensitiveWordMasker
{
    public MaskMessageResult Mask(string message, IEnumerable<SensitiveWordCandidate> sensitiveWords)
    {
        if (message.Length == 0)
        {
            return new MaskMessageResult(message, message, []);
        }

        var candidates = sensitiveWords
            .Select(word => word.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(value => value.Length)
            .ToArray();

        var reserved = new bool[message.Length];
        var matches = new List<SensitiveWordMatch>();

        foreach (var candidate in candidates)
        {
            var pattern = BuildPattern(candidate);
            foreach (Match match in Regex.Matches(message, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                if (IsRangeReserved(reserved, match.Index, match.Length))
                {
                    continue;
                }

                MarkRangeReserved(reserved, match.Index, match.Length);
                matches.Add(new SensitiveWordMatch(candidate, match.Index, match.Index + match.Length));
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

    private static string BuildPattern(string candidate)
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
}
