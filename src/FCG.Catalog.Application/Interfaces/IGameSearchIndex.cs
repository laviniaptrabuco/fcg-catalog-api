using FCG.Catalog.Application.DTOs;
using FCG.Catalog.Domain.Entities;

namespace FCG.Catalog.Application.Interfaces;

/// <summary>
/// Motor de busca (Elasticsearch/OpenSearch), independente do banco relacional.
/// O SQL Server continua sendo a fonte da verdade; o índice é uma projeção de leitura.
/// </summary>
public interface IGameSearchIndex
{
    Task EnsureIndexAsync(CancellationToken ct = default);
    Task IndexAsync(Game game, CancellationToken ct = default);
    Task IndexManyAsync(IEnumerable<Game> games, CancellationToken ct = default);
    Task<long> CountAsync(CancellationToken ct = default);
    Task<GameSearchPage> SearchAsync(string query, string? genre, int page, int size, CancellationToken ct = default);
}
