using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Api.SensitiveWords;

public interface ISensitiveWordAdministrationService
{
    Task<SensitiveWordDto> CreateAsync(CreateSensitiveWordRequest request, CancellationToken cancellationToken);

    Task<PagedResponse<SensitiveWordDto>> ListAsync(SensitiveWordQuery query, CancellationToken cancellationToken);

    Task<SensitiveWordDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<SensitiveWordDto?> UpdateAsync(Guid id, UpdateSensitiveWordRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
