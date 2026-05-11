using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Api.SensitiveWords;

public sealed class SensitiveWordAdministrationService(
    ISensitiveWordRepository repository,
    ISensitiveWordMatcherCache matcherCache) : ISensitiveWordAdministrationService
{
    public async Task<SensitiveWordDto> CreateAsync(CreateSensitiveWordRequest request, CancellationToken cancellationToken)
    {
        var created = await repository.CreateAsync(request, cancellationToken);
        matcherCache.Invalidate();
        return created;
    }

    public Task<PagedResponse<SensitiveWordDto>> ListAsync(SensitiveWordQuery query, CancellationToken cancellationToken)
    {
        return repository.ListAsync(query, cancellationToken);
    }

    public Task<SensitiveWordDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return repository.GetAsync(id, cancellationToken);
    }

    public async Task<SensitiveWordDto?> UpdateAsync(
        Guid id,
        UpdateSensitiveWordRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await repository.UpdateAsync(id, request, cancellationToken);
        if (updated is not null)
        {
            matcherCache.Invalidate();
        }

        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(id, cancellationToken);
        if (deleted)
        {
            matcherCache.Invalidate();
        }

        return deleted;
    }
}
