using FlashInterview.Application.SensitiveWords;
using Microsoft.EntityFrameworkCore;

namespace FlashInterview.Infrastructure.SensitiveWords;

public sealed class SqlSensitiveWordRepository(FlashInterviewDbContext dbContext) : ISensitiveWordRepository
{
    public async Task<SensitiveWordDto> CreateAsync(CreateSensitiveWordRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new SensitiveWordEntity
        {
            Id = Guid.NewGuid(),
            Value = request.Value.Trim(),
            NormalizedValue = SensitiveWordNormalizer.Normalize(request.Value),
            Category = NormalizeCategory(request.Category),
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.SensitiveWords.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Map(entity);
    }

    public async Task<PagedResponse<SensitiveWordDto>> ListAsync(SensitiveWordQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var words = dbContext.SensitiveWords.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var search = SensitiveWordNormalizer.Normalize(query.Q);
            words = words.Where(word => word.NormalizedValue.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = NormalizeCategory(query.Category);
            words = words.Where(word => word.Category == category);
        }

        if (query.IsActive is not null)
        {
            words = words.Where(word => word.IsActive == query.IsActive);
        }

        var total = await words.CountAsync(cancellationToken);
        var items = await words
            .OrderBy(word => word.Value)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(word => Map(word))
            .ToArrayAsync(cancellationToken);

        return new PagedResponse<SensitiveWordDto>(items, page, pageSize, total);
    }

    public async Task<SensitiveWordDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.SensitiveWords.AsNoTracking().SingleOrDefaultAsync(word => word.Id == id, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<SensitiveWordDto?> UpdateAsync(Guid id, UpdateSensitiveWordRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.SensitiveWords.SingleOrDefaultAsync(word => word.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Value = request.Value.Trim();
        entity.NormalizedValue = SensitiveWordNormalizer.Normalize(request.Value);
        entity.Category = NormalizeCategory(request.Category);
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Map(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.SensitiveWords.SingleOrDefaultAsync(word => word.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.SensitiveWords.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<SensitiveWordCandidate>> ListActiveCandidatesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.SensitiveWords
            .AsNoTracking()
            .Where(word => word.IsActive)
            .Select(word => new SensitiveWordCandidate(word.Value))
            .ToArrayAsync(cancellationToken);
    }

    private static string? NormalizeCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ? null : category.Trim().ToLowerInvariant();
    }

    private static SensitiveWordDto Map(SensitiveWordEntity entity)
    {
        return new SensitiveWordDto(
            entity.Id,
            entity.Value,
            entity.NormalizedValue,
            entity.Category,
            entity.IsActive,
            entity.CreatedAt,
            entity.UpdatedAt);
    }
}
