namespace FCG.Catalog.Application.DTOs;

public record CreateGameRequest(string Title, string Description, string Genre, decimal Price);
public record UpdateGameRequest(string Title, string Description, string Genre, decimal Price);
public record ApplyPromotionRequest(decimal PromotionalPrice);

public record GameResponse(
    Guid Id,
    string Title,
    string Description,
    string Genre,
    decimal Price,
    decimal? PromotionalPrice,
    decimal CurrentPrice,
    bool IsActive,
    DateTime CreatedAt);

public record GameSearchHit(
    Guid Id,
    string Title,
    string Description,
    string Genre,
    decimal Price,
    decimal? PromotionalPrice,
    decimal CurrentPrice,
    bool IsActive,
    DateTime CreatedAt,
    double? Score);

public record GameSearchPage(long Total, IReadOnlyList<GameSearchHit> Hits);

public record SearchResponse(string Query, string? Genre, int Page, int Size, long Total, IReadOnlyList<GameSearchHit> Results);
