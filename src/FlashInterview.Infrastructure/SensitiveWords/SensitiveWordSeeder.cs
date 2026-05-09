using FlashInterview.Application.SensitiveWords;
using Microsoft.EntityFrameworkCore;

namespace FlashInterview.Infrastructure.SensitiveWords;

public sealed class SensitiveWordSeeder(FlashInterviewDbContext dbContext)
{
    public async Task<int> SeedFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Sensitive word seed file was not found.", filePath);
        }

        var source = await File.ReadAllTextAsync(filePath, cancellationToken);
        var entries = SensitiveWordSeedParser.Parse(source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var existing = await dbContext.SensitiveWords
            .Select(word => word.NormalizedValue)
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;
        var newWords = entries
            .Where(entry => !existingSet.Contains(SensitiveWordNormalizer.Normalize(entry)))
            .Select(entry => new SensitiveWordEntity
            {
                Id = Guid.NewGuid(),
                Value = entry,
                NormalizedValue = SensitiveWordNormalizer.Normalize(entry),
                Category = "sql",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            })
            .ToArray();

        if (newWords.Length == 0)
        {
            return 0;
        }

        dbContext.SensitiveWords.AddRange(newWords);
        await dbContext.SaveChangesAsync(cancellationToken);

        return newWords.Length;
    }
}
