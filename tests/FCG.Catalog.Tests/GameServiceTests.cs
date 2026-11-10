using FCG.Catalog.Application.Caching;
using FCG.Catalog.Application.DTOs;
using FCG.Catalog.Application.Interfaces;
using FCG.Catalog.Application.Services;
using FCG.Catalog.Domain.Entities;
using FCG.Catalog.Domain.Exceptions;
using FCG.Catalog.Domain.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FCG.Catalog.Tests;

public class GameServiceTests
{
    private readonly Mock<IGameRepository> _repo = new();
    private readonly Mock<IGameSearchIndex> _index = new();
    private readonly CatalogCache _cache = new(
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        NullLogger<CatalogCache>.Instance);

    private GameService CreateSut() =>
        new(_repo.Object, _index.Object, _cache, new CatalogCacheOptions(), NullLogger<GameService>.Instance);

    private static GameSearchPage OneHit(string title) =>
        new(1, new[] { new GameSearchHit(Guid.NewGuid(), title, "d", "RPG", 100m, null, 100m, true, DateTime.UtcNow, 7.5) });

    // ---------- sincronizacao com o Elasticsearch ----------

    [Fact]
    public async Task Create_PersistsInDatabaseThenIndexesInElasticsearch()
    {
        _repo.Setup(r => r.ExistsByTitleAsync("Witcher", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateSut().CreateAsync(new CreateGameRequest("Witcher", "RPG", "RPG", 100m));

        _repo.Verify(r => r.AddAsync(It.IsAny<Game>(), It.IsAny<CancellationToken>()), Times.Once);
        _index.Verify(i => i.IndexAsync(It.Is<Game>(g => g.Id == result.Id && g.Title == "Witcher"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateTitle_DoesNotIndex()
    {
        _repo.Setup(r => r.ExistsByTitleAsync("Witcher", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<DomainException>(() =>
            CreateSut().CreateAsync(new CreateGameRequest("Witcher", "RPG", "RPG", 100m)));

        _index.Verify(i => i.IndexAsync(It.IsAny<Game>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_ReindexesTheEditedGame()
    {
        var game = new Game("Witcher", "d", "RPG", 100m);
        _repo.Setup(r => r.GetByIdAsync(game.Id, It.IsAny<CancellationToken>())).ReturnsAsync(game);

        await CreateSut().UpdateAsync(game.Id, new UpdateGameRequest("Witcher 3", "d2", "RPG", 120m));

        _index.Verify(i => i.IndexAsync(It.Is<Game>(g => g.Title == "Witcher 3" && g.Price == 120m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyPromotion_Deactivate_AndRemovePromotion_AllReindex()
    {
        var game = new Game("Witcher", "d", "RPG", 100m);
        _repo.Setup(r => r.GetByIdAsync(game.Id, It.IsAny<CancellationToken>())).ReturnsAsync(game);
        var sut = CreateSut();

        await sut.ApplyPromotionAsync(game.Id, new ApplyPromotionRequest(50m));
        await sut.RemovePromotionAsync(game.Id);
        await sut.DeactivateAsync(game.Id);

        _index.Verify(i => i.IndexAsync(game, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Update_UnknownGame_ThrowsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Game?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateSut().UpdateAsync(Guid.NewGuid(), new UpdateGameRequest("x", "d", "RPG", 1m)));
    }

    [Fact]
    public async Task Create_WhenElasticsearchIsDown_StillSucceeds()
    {
        _repo.Setup(r => r.ExistsByTitleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _index.Setup(i => i.IndexAsync(It.IsAny<Game>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("elasticsearch indisponivel"));

        var result = await CreateSut().CreateAsync(new CreateGameRequest("Witcher", "RPG", "RPG", 100m));

        Assert.Equal("Witcher", result.Title);
        _repo.Verify(r => r.AddAsync(It.IsAny<Game>(), It.IsAny<CancellationToken>()), Times.Once); // banco e a fonte da verdade
    }

    // ---------- busca ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_RequiresQuery(string? q) =>
        await Assert.ThrowsAsync<DomainException>(() => CreateSut().SearchAsync(q, null, 1, 10));

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task Search_ValidatesPaging(int page, int size) =>
        await Assert.ThrowsAsync<DomainException>(() => CreateSut().SearchAsync("witcher", null, page, size));

    [Fact]
    public async Task Search_DelegatesToElasticsearchWithTrimmedQueryAndPaging()
    {
        _index.Setup(i => i.SearchAsync("wiitcher", "RPG", 2, 5, It.IsAny<CancellationToken>())).ReturnsAsync(OneHit("The Witcher 3"));

        var result = await CreateSut().SearchAsync("  wiitcher ", " RPG ", 2, 5);

        Assert.False(result.Hit);
        Assert.Equal("wiitcher", result.Value.Query);
        Assert.Equal(1, result.Value.Total);
        Assert.Equal("The Witcher 3", result.Value.Results[0].Title);
    }

    [Fact]
    public async Task Search_SecondIdenticalQueryIsServedFromRedis()
    {
        _index.Setup(i => i.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(OneHit("The Witcher 3"));
        var sut = CreateSut();

        var first = await sut.SearchAsync("witcher", null, 1, 10);
        var second = await sut.SearchAsync("WITCHER", null, 1, 10); // mesma consulta normalizada

        Assert.False(first.Hit);
        Assert.True(second.Hit);
        _index.Verify(i => i.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Write_InvalidatesCachedSearchAndList()
    {
        _index.Setup(i => i.SearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(OneHit("The Witcher 3"));
        _repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Game>());
        _repo.Setup(r => r.ExistsByTitleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var sut = CreateSut();

        await sut.SearchAsync("witcher", null, 1, 10);
        await sut.GetAllAsync();
        await sut.CreateAsync(new CreateGameRequest("Cyberpunk", "RPG", "RPG", 200m));

        Assert.False((await sut.SearchAsync("witcher", null, 1, 10)).Hit);
        Assert.False((await sut.GetAllAsync()).Hit);
    }

    // ---------- listagem em cache ----------

    [Fact]
    public async Task GetAll_SecondCallComesFromRedisWithoutHittingTheDatabase()
    {
        _repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<Game> { new("Witcher", "d", "RPG", 100m) });
        var sut = CreateSut();

        var first = await sut.GetAllAsync();
        var second = await sut.GetAllAsync();

        Assert.False(first.Hit);
        Assert.True(second.Hit);
        Assert.Single(second.Value);
        _repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
