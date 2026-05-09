using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Tests;

public sealed class SensitiveWordMaskerTests
{
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
}
