using FCG.Catalog.Application.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FCG.Catalog.Tests;

public class CatalogCacheTests
{
    private static IDistributedCache NewMemoryCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static CatalogCache NewSut(IDistributedCache? cache = null) =>
        new(cache ?? NewMemoryCache(), NullLogger<CatalogCache>.Instance);

    [Fact]
    public async Task GetOrCreate_FirstCallMissesSecondCallHits()
    {
        var sut = NewSut();
        var calls = 0;
        Task<int> Factory() { calls++; return Task.FromResult(42); }

        var first = await sut.GetOrCreateAsync("games", "all", TimeSpan.FromMinutes(1), Factory);
        var second = await sut.GetOrCreateAsync("games", "all", TimeSpan.FromMinutes(1), Factory);

        Assert.False(first.Hit);
        Assert.True(second.Hit);
        Assert.Equal(42, second.Value);
        Assert.Equal(1, calls); // a segunda leitura nao tocou na fonte
    }

    [Fact]
    public async Task GetOrCreate_DifferentKeysDoNotCollide()
    {
        var sut = NewSut();
        await sut.GetOrCreateAsync("search", "a", TimeSpan.FromMinutes(1), () => Task.FromResult("A"));
        var other = await sut.GetOrCreateAsync("search", "b", TimeSpan.FromMinutes(1), () => Task.FromResult("B"));

        Assert.False(other.Hit);
        Assert.Equal("B", other.Value);
    }

    [Fact]
    public async Task Invalidate_ForcesNextReadToHitTheSource()
    {
        var sut = NewSut();
        var calls = 0;
        Task<int> Factory() { calls++; return Task.FromResult(calls); }

        await sut.GetOrCreateAsync("games", "all", TimeSpan.FromMinutes(1), Factory);
        await sut.InvalidateAsync();
        var afterWrite = await sut.GetOrCreateAsync("games", "all", TimeSpan.FromMinutes(1), Factory);

        Assert.False(afterWrite.Hit);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetOrCreate_FallsBackToSourceWhenCacheIsDown()
    {
        var broken = new Mock<IDistributedCache>();
        broken.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("redis down"));
        broken.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("redis down"));

        var result = await NewSut(broken.Object)
            .GetOrCreateAsync("games", "all", TimeSpan.FromMinutes(1), () => Task.FromResult("do-banco"));

        Assert.False(result.Hit);
        Assert.Equal("do-banco", result.Value); // a API continua respondendo sem Redis
    }

    [Fact]
    public async Task Invalidate_DoesNotThrowWhenCacheIsDown()
    {
        var broken = new Mock<IDistributedCache>();
        broken.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("redis down"));

        await NewSut(broken.Object).InvalidateAsync(); // nao deve lancar
    }
}
