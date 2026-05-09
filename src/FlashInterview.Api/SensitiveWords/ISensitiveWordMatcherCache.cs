using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Api.SensitiveWords;

public interface ISensitiveWordMatcherCache
{
    Task<CompiledSensitiveWordMasker> GetAsync(CancellationToken cancellationToken);

    void Invalidate();
}
