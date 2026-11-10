using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FCG.Catalog.Application.Caching;

public record CachedResult<T>(T Value, bool Hit);

public class CatalogCacheOptions
{
    public int ListTtlSeconds { get; set; } = 300;
    public int SearchTtlSeconds { get; set; } = 120;
}

/// <summary>
/// Cache distribuído (Redis via IDistributedCache) para consultas onerosas do catálogo.
/// A invalidação usa um "token de versão": toda escrita no catálogo troca o token, e como
/// ele faz parte de todas as chaves, as entradas antigas deixam de ser lidas e expiram
/// sozinhas pelo TTL (sem precisar enumerar/apagar chaves no Redis).
/// Se o Redis estiver indisponível, o cache é ignorado e a consulta vai ao banco.
/// </summary>
public class CatalogCache
{
    private const string VersionKey = "catalog:version";
    private readonly IDistributedCache _cache;
    private readonly ILogger<CatalogCache> _logger;

    public CatalogCache(IDistributedCache cache, ILogger<CatalogCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<CachedResult<T>> GetOrCreateAsync<T>(
        string scope, string key, TimeSpan ttl, Func<Task<T>> factory, CancellationToken ct = default)
    {
        string? fullKey = null;
        try
        {
            var version = await _cache.GetStringAsync(VersionKey, ct) ?? "0";
            fullKey = $"catalog:{scope}:{version}:{key}";
            var cached = await _cache.GetStringAsync(fullKey, ct);
            if (cached is not null)
            {
                var value = JsonSerializer.Deserialize<T>(cached);
                if (value is not null) return new CachedResult<T>(value, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache indisponivel na leitura; consultando a fonte.");
        }

        var fresh = await factory();

        if (fullKey is not null)
        {
            try
            {
                await _cache.SetStringAsync(fullKey, JsonSerializer.Serialize(fresh),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache indisponivel na escrita; resposta segue sem cachear.");
            }
        }

        return new CachedResult<T>(fresh, false);
    }

    /// <summary>Invalida todas as consultas em cache trocando o token de versão.</summary>
    public async Task InvalidateAsync(CancellationToken ct = default)
    {
        try
        {
            await _cache.SetStringAsync(VersionKey, Guid.NewGuid().ToString("N"),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao invalidar o cache do catalogo.");
        }
    }
}
