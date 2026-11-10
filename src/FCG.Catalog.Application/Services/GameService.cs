using System.Security.Cryptography;
using System.Text;
using FCG.Catalog.Application.Caching;
using FCG.Catalog.Application.DTOs;
using FCG.Catalog.Application.Interfaces;
using FCG.Catalog.Domain.Exceptions;
using FCG.Catalog.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FCG.Catalog.Application.Services;

public class GameService
{
    public const int MaxPageSize = 50;

    private readonly IGameRepository _gameRepository;
    private readonly IGameSearchIndex _searchIndex;
    private readonly CatalogCache _cache;
    private readonly CatalogCacheOptions _cacheOptions;
    private readonly ILogger<GameService> _logger;

    public GameService(
        IGameRepository gameRepository,
        IGameSearchIndex searchIndex,
        CatalogCache cache,
        CatalogCacheOptions cacheOptions,
        ILogger<GameService> logger)
    {
        _gameRepository = gameRepository;
        _searchIndex = searchIndex;
        _cache = cache;
        _cacheOptions = cacheOptions;
        _logger = logger;
    }

    /// <summary>Lista completa: consulta no SQL Server, servida pelo Redis quando possível.</summary>
    public Task<CachedResult<IReadOnlyList<GameResponse>>> GetAllAsync(CancellationToken ct = default) =>
        _cache.GetOrCreateAsync<IReadOnlyList<GameResponse>>(
            "games", "all", TimeSpan.FromSeconds(_cacheOptions.ListTtlSeconds),
            async () => (await _gameRepository.GetAllAsync(ct)).Select(MapToResponse).ToList(), ct);

    public async Task<GameResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var game = await _gameRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Game), id);
        return MapToResponse(game);
    }

    public async Task<IEnumerable<GameResponse>> GetByGenreAsync(string genre, CancellationToken ct = default)
    {
        var games = await _gameRepository.GetByGenreAsync(genre, ct);
        return games.Select(MapToResponse);
    }

    /// <summary>
    /// Busca avançada no Elasticsearch: fuzzy (tolerância a erro de digitação) e ordenação
    /// por relevância. Resultados são cacheados no Redis por consulta normalizada.
    /// </summary>
    public Task<CachedResult<SearchResponse>> SearchAsync(
        string? query, string? genre, int page, int size, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new DomainException("Query parameter 'q' is required.");
        if (page < 1)
            throw new DomainException("Page must be greater than or equal to 1.");
        if (size < 1 || size > MaxPageSize)
            throw new DomainException($"Size must be between 1 and {MaxPageSize}.");

        var q = query.Trim();
        var g = string.IsNullOrWhiteSpace(genre) ? null : genre.Trim();
        var key = Hash($"{q.ToLowerInvariant()}|{g?.ToLowerInvariant()}|{page}|{size}");

        return _cache.GetOrCreateAsync(
            "search", key, TimeSpan.FromSeconds(_cacheOptions.SearchTtlSeconds),
            async () =>
            {
                var result = await _searchIndex.SearchAsync(q, g, page, size, ct);
                return new SearchResponse(q, g, page, size, result.Total, result.Hits);
            }, ct);
    }

    public async Task<GameResponse> CreateAsync(CreateGameRequest request, CancellationToken ct = default)
    {
        if (await _gameRepository.ExistsByTitleAsync(request.Title, ct))
            throw new DomainException($"A game with title '{request.Title}' already exists.");

        var game = new Domain.Entities.Game(request.Title, request.Description, request.Genre, request.Price);
        await _gameRepository.AddAsync(game, ct);
        _logger.LogInformation("Game '{Title}' created.", game.Title);
        await SyncAsync(game, ct);
        return MapToResponse(game);
    }

    public async Task<GameResponse> UpdateAsync(Guid id, UpdateGameRequest request, CancellationToken ct = default)
    {
        var game = await _gameRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Game), id);

        game.Update(request.Title, request.Description, request.Genre, request.Price);
        await _gameRepository.UpdateAsync(game, ct);
        await SyncAsync(game, ct);
        return MapToResponse(game);
    }

    public async Task<GameResponse> ApplyPromotionAsync(Guid id, ApplyPromotionRequest request, CancellationToken ct = default)
    {
        var game = await _gameRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Game), id);

        game.ApplyPromotion(request.PromotionalPrice);
        await _gameRepository.UpdateAsync(game, ct);
        await SyncAsync(game, ct);
        return MapToResponse(game);
    }

    public async Task RemovePromotionAsync(Guid id, CancellationToken ct = default)
    {
        var game = await _gameRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Game), id);
        game.RemovePromotion();
        await _gameRepository.UpdateAsync(game, ct);
        await SyncAsync(game, ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var game = await _gameRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Game), id);
        game.Deactivate();
        await _gameRepository.UpdateAsync(game, ct);
        await SyncAsync(game, ct);
    }

    /// <summary>
    /// Mantém o índice do Elasticsearch e o cache coerentes com o banco principal.
    /// O banco é a fonte da verdade: se o Elasticsearch estiver fora do ar a escrita NÃO falha;
    /// o reindex automático na subida da API (SearchIndexInitializer) recompõe o índice.
    /// </summary>
    private async Task SyncAsync(Domain.Entities.Game game, CancellationToken ct)
    {
        try
        {
            await _searchIndex.IndexAsync(game, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao indexar o jogo {GameId} no Elasticsearch; sera recomposto no proximo reindex.", game.Id);
        }

        await _cache.InvalidateAsync(ct);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];

    private static GameResponse MapToResponse(Domain.Entities.Game g) =>
        new(g.Id, g.Title, g.Description, g.Genre, g.Price, g.PromotionalPrice, g.CurrentPrice, g.IsActive, g.CreatedAt);
}
