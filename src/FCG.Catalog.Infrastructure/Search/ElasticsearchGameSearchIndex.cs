using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using FCG.Catalog.Application.DTOs;
using FCG.Catalog.Application.Interfaces;
using FCG.Catalog.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FCG.Catalog.Infrastructure.Search;

public class ElasticsearchGameSearchIndex : IGameSearchIndex
{
    private readonly ElasticsearchClient _client;
    private readonly string _index;
    private readonly ILogger<ElasticsearchGameSearchIndex> _logger;

    public ElasticsearchGameSearchIndex(
        ElasticsearchClient client,
        IOptions<ElasticsearchOptions> options,
        ILogger<ElasticsearchGameSearchIndex> logger)
    {
        _client = client;
        _index = options.Value.IndexName;
        _logger = logger;
    }

    public async Task EnsureIndexAsync(CancellationToken ct = default)
    {
        var exists = await _client.Indices.ExistsAsync(_index, ct);
        if (exists.Exists) return;

        var created = await _client.Indices.CreateAsync(_index, c => c
            .Mappings(m => m
                .Properties(new Properties<GameSearchDocument>
                {
                    { "id", new KeywordProperty() },
                    { "title", new TextProperty { Fields = new Properties { { "raw", new KeywordProperty() } } } },
                    { "description", new TextProperty() },
                    { "genre", new TextProperty() },
                    { "genreKey", new KeywordProperty() },
                    { "price", new DoubleNumberProperty() },
                    { "promotionalPrice", new DoubleNumberProperty() },
                    { "currentPrice", new DoubleNumberProperty() },
                    { "isActive", new BooleanProperty() },
                    { "createdAt", new DateProperty() }
                })), ct);

        // Corrida entre réplicas criando o índice ao mesmo tempo não é erro.
        if (!created.IsValidResponse && created.ElasticsearchServerError?.Error?.Type != "resource_already_exists_exception")
            throw new InvalidOperationException($"Falha ao criar o indice '{_index}': {created.DebugInformation}");

        _logger.LogInformation("Indice Elasticsearch '{Index}' criado.", _index);
    }

    public async Task IndexAsync(Game game, CancellationToken ct = default)
    {
        var doc = GameSearchDocument.From(game);
        var response = await _client.IndexAsync(doc, i => i.Index(_index).Id(doc.Id), ct);
        if (!response.IsValidResponse)
            throw new InvalidOperationException($"Falha ao indexar o jogo {doc.Id}: {response.DebugInformation}");
    }

    public async Task IndexManyAsync(IEnumerable<Game> games, CancellationToken ct = default)
    {
        var docs = games.Select(GameSearchDocument.From).ToList();
        if (docs.Count == 0) return;

        var response = await _client.BulkAsync(b => b
            .Index(_index)
            .IndexMany(docs, (op, d) => op.Id(d.Id)), ct);

        if (!response.IsValidResponse || response.Errors)
            throw new InvalidOperationException($"Falha na indexacao em lote: {response.DebugInformation}");
    }

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        var response = await _client.CountAsync(c => c.Indices(_index), ct);
        return response.IsValidResponse ? response.Count : 0;
    }

    public async Task<GameSearchPage> SearchAsync(string query, string? genre, int page, int size, CancellationToken ct = default)
    {
        var filters = new List<Query> { new TermQuery("isActive") { Value = true } };
        if (!string.IsNullOrWhiteSpace(genre))
            filters.Add(new TermQuery("genreKey") { Value = genre.Trim().ToLowerInvariant() });

        // Fuzzy Search: Fuzziness AUTO tolera erros de digitação (0 edicoes p/ ate 2 letras,
        // 1 edicao de 3 a 5, 2 edicoes acima disso). O titulo pesa mais que genero e descricao.
        // O phrase_prefix extra no titulo dá bônus a quem digitou o início do nome ("witc").
        var fuzzy = new MultiMatchQuery
        {
            Query = query,
            Fields = new[] { "title^3", "genre^2", "description" },
            Fuzziness = new Fuzziness("AUTO"),
            PrefixLength = 1,
            Operator = Operator.Or
        };

        var prefix = new MatchPhrasePrefixQuery("title") { Query = query, Boost = 4 };

        var request = new SearchRequest(_index)
        {
            From = (page - 1) * size,
            Size = size,
            TrackTotalHits = new TrackHits(true),
            Query = new BoolQuery
            {
                Must = new Query[]
                {
                    new BoolQuery { Should = new Query[] { fuzzy, prefix }, MinimumShouldMatch = 1 }
                },
                Filter = filters
            },
            // Relevância: _score decrescente; desempate pelos mais recentes.
            Sort = new List<SortOptions>
            {
                SortOptions.Score(new ScoreSort { Order = SortOrder.Desc }),
                SortOptions.Field(new Field("createdAt"), new FieldSort { Order = SortOrder.Desc })
            }
        };

        var response = await _client.SearchAsync<GameSearchDocument>(request, ct);

        if (!response.IsValidResponse)
            throw new InvalidOperationException($"Falha na busca: {response.DebugInformation}");

        var hits = response.Hits
            .Where(h => h.Source is not null)
            .Select(h => ToHit(h.Source!, h.Score))
            .ToList();

        return new GameSearchPage(response.Total, hits);
    }

    private static GameSearchHit ToHit(GameSearchDocument d, double? score) => new(
        Guid.Parse(d.Id),
        d.Title,
        d.Description,
        d.Genre,
        (decimal)d.Price,
        d.PromotionalPrice is null ? null : (decimal)d.PromotionalPrice.Value,
        (decimal)d.CurrentPrice,
        d.IsActive,
        d.CreatedAt,
        score);
}
