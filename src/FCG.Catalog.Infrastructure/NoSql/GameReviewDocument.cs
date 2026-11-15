using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FCG.Catalog.Infrastructure.NoSql;

[BsonIgnoreExtraElements]
public class GameReviewDocument
{
    // Sem [BsonIgnoreIfDefault] o driver grava "_id: null" explicitamente
    // quando Id nao e setado - a segunda review inserida bate no indice
    // unico _id_ com o mesmo _id null (DuplicateKey, code 11000).
    [BsonId]
    [BsonIgnoreIfDefault]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("gameId")]
    [BsonRepresentation(BsonType.String)]
    public Guid GameId { get; set; }

    [BsonElement("userId")]
    [BsonRepresentation(BsonType.String)]
    public Guid UserId { get; set; }

    [BsonElement("rating")]
    public int Rating { get; set; }

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("comment")]
    public string? Comment { get; set; }

    [BsonElement("tags")]
    public List<string> Tags { get; set; } = new();

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record GameRatingSummary(Guid GameId, double AverageRating, int ReviewCount);
