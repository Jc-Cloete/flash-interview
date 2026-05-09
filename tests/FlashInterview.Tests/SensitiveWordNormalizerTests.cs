using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Tests;

public sealed class SensitiveWordNormalizerTests
{
    [Theory]
    [InlineData(" select ", "SELECT")]
    [InlineData("\tCurrent_User\r\n", "CURRENT_USER")]
    [InlineData("Select * From", "SELECT * FROM")]
    public void Normalize_TrimsAndUppercasesInvariantly(string value, string expected)
    {
        var normalized = SensitiveWordNormalizer.Normalize(value);

        Assert.Equal(expected, normalized);
    }
}
