using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Tests;

public sealed class SensitiveWordMaskerTests
{
    [Fact]
    public void CompiledMasker_ReusesPreparedPatternsAcrossMessages()
    {
        var compiled = CompiledSensitiveWordMasker.FromCandidates(
        [
            new SensitiveWordCandidate("DROP"),
            new SensitiveWordCandidate("SELECT * FROM")
        ]);

        var first = compiled.Mask("DROP table users");
        var second = compiled.Mask("SELECT * FROM users");

        Assert.Equal("**** table users", first.MaskedMessage);
        Assert.Equal("************* users", second.MaskedMessage);
    }

    [Fact]
    public void Mask_ReplacesConfiguredWordsCaseInsensitively()
    {
        var masker = new SensitiveWordMasker();
        var words = new[]
        {
            new SensitiveWordCandidate("SELECT"),
            new SensitiveWordCandidate("DROP")
        };

        var result = masker.Mask("select then DROP", words);

        Assert.Equal("****** then ****", result.MaskedMessage);
    }

    [Fact]
    public void Mask_DoesNotReplaceConfiguredWordInsideAnotherWord()
    {
        var masker = new SensitiveWordMasker();
        var words = new[] { new SensitiveWordCandidate("DROP") };

        var result = masker.Mask("DROPLET DROP", words);

        Assert.Equal("DROPLET ****", result.MaskedMessage);
    }

    [Fact]
    public void Mask_UsesLongestMatchWhenPhrasesOverlap()
    {
        var masker = new SensitiveWordMasker();
        var words = new[]
        {
            new SensitiveWordCandidate("SELECT"),
            new SensitiveWordCandidate("FROM"),
            new SensitiveWordCandidate("SELECT * FROM")
        };

        var result = masker.Mask("SELECT * FROM users", words);

        Assert.Equal("************* users", result.MaskedMessage);
        Assert.Single(result.Matches);
        Assert.Equal("SELECT * FROM", result.Matches[0].Value);
    }

    [Fact]
    public void Mask_UsesDeterministicTieBreakForSameLengthOverlaps()
    {
        var masker = new SensitiveWordMasker();
        var words = new[]
        {
            new SensitiveWordCandidate("B C"),
            new SensitiveWordCandidate("A B")
        };

        var result = masker.Mask("A B C", words);

        Assert.Equal("*** C", result.MaskedMessage);
        Assert.Equal(new SensitiveWordMatch("A B", 0, 3), Assert.Single(result.Matches));
    }

    [Fact]
    public void Mask_IgnoresBlankCandidatesAndDeduplicatesWords()
    {
        var masker = new SensitiveWordMasker();
        var words = new[]
        {
            new SensitiveWordCandidate(" "),
            new SensitiveWordCandidate(" drop "),
            new SensitiveWordCandidate("DROP")
        };

        var result = masker.Mask("drop DROP", words);

        Assert.Equal("**** ****", result.MaskedMessage);
        Assert.Equal(
            [
                new SensitiveWordMatch("drop", 0, 4),
                new SensitiveWordMatch("drop", 5, 9)
            ],
            result.Matches);
    }

    [Fact]
    public void Mask_HandlesRegexCharactersLiterally()
    {
        var masker = new SensitiveWordMasker();
        var words = new[] { new SensitiveWordCandidate("SELECT * FROM") };

        var result = masker.Mask("SELECT a FROM users; SELECT * FROM secrets", words);

        Assert.Equal("SELECT a FROM users; ************* secrets", result.MaskedMessage);
        Assert.Single(result.Matches);
        Assert.Equal(new SensitiveWordMatch("SELECT * FROM", 21, 34), result.Matches[0]);
    }

    [Theory]
    [InlineData("DROP,DROP.", "****,****.")]
    [InlineData("_DROP DROP DROP_TABLE", "_DROP **** DROP_TABLE")]
    [InlineData("DROP-table", "****-table")]
    public void Mask_OnlyAppliesSingleWordBoundaryRulesToSingleWordCandidates(string message, string expected)
    {
        var masker = new SensitiveWordMasker();
        var words = new[] { new SensitiveWordCandidate("DROP") };

        var result = masker.Mask(message, words);

        Assert.Equal(expected, result.MaskedMessage);
    }

    [Fact]
    public void Mask_ReturnsEmptyMessageWithoutMatches()
    {
        var masker = new SensitiveWordMasker();
        var words = new[] { new SensitiveWordCandidate("DROP") };

        var result = masker.Mask(string.Empty, words);

        Assert.Equal(string.Empty, result.OriginalMessage);
        Assert.Equal(string.Empty, result.MaskedMessage);
        Assert.Empty(result.Matches);
    }
}
