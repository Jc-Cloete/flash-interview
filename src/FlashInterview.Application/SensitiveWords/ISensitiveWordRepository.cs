namespace FlashInterview.Application.SensitiveWords;

public interface ISensitiveWordRepository
{
    Task<SensitiveWordDto> CreateAsync(CreateSensitiveWordRequest request, CancellationToken cancellationToken);

    Task<PagedResponse<SensitiveWordDto>> ListAsync(SensitiveWordQuery query, CancellationToken cancellationToken);

    Task<SensitiveWordDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<SensitiveWordDto?> UpdateAsync(Guid id, UpdateSensitiveWordRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<SensitiveWordCandidate>> ListActiveCandidatesAsync(CancellationToken cancellationToken);
}
