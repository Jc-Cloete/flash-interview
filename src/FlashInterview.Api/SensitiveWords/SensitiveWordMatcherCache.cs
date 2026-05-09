using FlashInterview.Application.SensitiveWords;

namespace FlashInterview.Api.SensitiveWords;

public sealed class SensitiveWordMatcherCache(IServiceScopeFactory scopeFactory) : ISensitiveWordMatcherCache
{
    private readonly object cacheLock = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private volatile CacheEntry? cachedEntry;
    private long generation;

    public async Task<CompiledSensitiveWordMasker> GetAsync(CancellationToken cancellationToken)
    {
        var current = cachedEntry;
        if (current is not null)
        {
            return current.Masker;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            current = cachedEntry;
            if (current is not null)
            {
                return current.Masker;
            }

            long refreshGeneration;
            lock (cacheLock)
            {
                current = cachedEntry;
                if (current is not null)
                {
                    return current.Masker;
                }

                refreshGeneration = generation;
            }

            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ISensitiveWordRepository>();
            var candidates = await repository.ListActiveCandidatesAsync(cancellationToken);

            var refreshed = CompiledSensitiveWordMasker.FromCandidates(candidates);
            lock (cacheLock)
            {
                if (generation == refreshGeneration)
                {
                    cachedEntry = new CacheEntry(refreshGeneration, refreshed);
                }
            }

            return refreshed;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public void Invalidate()
    {
        lock (cacheLock)
        {
            generation++;
            cachedEntry = null;
        }
    }

    private sealed record CacheEntry(long Generation, CompiledSensitiveWordMasker Masker);
}
