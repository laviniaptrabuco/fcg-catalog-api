using FCG.Catalog.Application.Interfaces;
using FCG.Catalog.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FCG.Catalog.Infrastructure.Search;

/// <summary>
/// Na subida da API: garante o índice e reindexa o catálogo do SQL Server quando o índice
/// está desatualizado (ex.: ambiente novo, Elasticsearch recriado, escrita que falhou).
/// Roda em background com retry, então a API não deixa de subir se o Elasticsearch demorar.
/// </summary>
public class SearchIndexInitializer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SearchIndexInitializer> _logger;

    public SearchIndexInitializer(IServiceScopeFactory scopeFactory, ILogger<SearchIndexInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        const int maxAttempts = 20;
        for (var attempt = 1; attempt <= maxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var index = scope.ServiceProvider.GetRequiredService<IGameSearchIndex>();
                var repository = scope.ServiceProvider.GetRequiredService<IGameRepository>();

                await index.EnsureIndexAsync(stoppingToken);

                var games = (await repository.GetAllAsync(stoppingToken)).ToList();
                var indexed = await index.CountAsync(stoppingToken);
                if (indexed != games.Count)
                {
                    _logger.LogInformation("Reindexando catalogo: {Db} no banco, {Es} no indice.", games.Count, indexed);
                    await index.IndexManyAsync(games, stoppingToken);
                }

                _logger.LogInformation("Indice de busca pronto ({Count} jogos).", games.Count);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Elasticsearch/SQL indisponivel (tentativa {Attempt}/{Max}); nova tentativa em 10s.", attempt, maxAttempts);
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
