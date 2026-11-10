using FCG.Catalog.Domain.Entities;
using FCG.Catalog.Domain.Exceptions;

namespace FCG.Catalog.Tests;

public class GameTests
{
    private static Game NewGame(decimal price = 100m) => new("The Witcher 3", "RPG de mundo aberto", "RPG", price);

    [Fact]
    public void Constructor_CreatesActiveGame()
    {
        var game = NewGame();
        Assert.True(game.IsActive);
        Assert.Equal(100m, game.CurrentPrice);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsEmptyTitle(string title) =>
        Assert.Throws<DomainException>(() => new Game(title, "d", "RPG", 10m));

    [Fact]
    public void Constructor_RejectsTitleOver200Chars() =>
        Assert.Throws<DomainException>(() => new Game(new string('x', 201), "d", "RPG", 10m));

    [Fact]
    public void Constructor_RejectsNegativePrice() =>
        Assert.Throws<DomainException>(() => new Game("Jogo", "d", "RPG", -1m));

    [Fact]
    public void ApplyPromotion_SetsCurrentPrice()
    {
        var game = NewGame();
        game.ApplyPromotion(60m);
        Assert.Equal(60m, game.CurrentPrice);
        Assert.Equal(100m, game.Price);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100)]
    [InlineData(150)]
    public void ApplyPromotion_RejectsInvalidPrices(double promo) =>
        Assert.Throws<DomainException>(() => NewGame().ApplyPromotion((decimal)promo));

    [Fact]
    public void RemovePromotion_RestoresOriginalPrice()
    {
        var game = NewGame();
        game.ApplyPromotion(60m);
        game.RemovePromotion();
        Assert.Equal(100m, game.CurrentPrice);
    }

    [Fact]
    public void Update_ChangesFieldsAndValidates()
    {
        var game = NewGame();
        game.Update("The Witcher 3: GOTY", "Edicao completa", "RPG", 120m);
        Assert.Equal("The Witcher 3: GOTY", game.Title);
        Assert.Equal(120m, game.Price);
        Assert.Throws<DomainException>(() => game.Update("", "d", "RPG", 1m));
    }

    [Fact]
    public void Deactivate_MarksGameInactive()
    {
        var game = NewGame();
        game.Deactivate();
        Assert.False(game.IsActive);
    }
}
