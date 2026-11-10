using FCG.Catalog.Domain.Entities;

namespace FCG.Catalog.Infrastructure.Search;

/// <summary>Projeção de um jogo no índice do Elasticsearch.</summary>
public class GameSearchDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;

    /// <summary>Gênero normalizado (minúsculo) usado no filtro exato.</summary>
    public string GenreKey { get; set; } = string.Empty;

    public double Price { get; set; }
    public double? PromotionalPrice { get; set; }
    public double CurrentPrice { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public static GameSearchDocument From(Game g) => new()
    {
        Id = g.Id.ToString(),
        Title = g.Title,
        Description = g.Description,
        Genre = g.Genre,
        GenreKey = (g.Genre ?? string.Empty).Trim().ToLowerInvariant(),
        Price = (double)g.Price,
        PromotionalPrice = g.PromotionalPrice is null ? null : (double)g.PromotionalPrice.Value,
        CurrentPrice = (double)g.CurrentPrice,
        IsActive = g.IsActive,
        CreatedAt = g.CreatedAt
    };
}
