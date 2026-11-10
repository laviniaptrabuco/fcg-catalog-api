using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using FCG.Catalog.Application.Caching;
using FCG.Catalog.Application.Interfaces;
using FCG.Catalog.Domain.Interfaces;
using FCG.Catalog.Infrastructure.Data;
using FCG.Catalog.Infrastructure.Repositories;
using FCG.Catalog.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FCG.Catalog.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<CatalogDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql => sql.MigrationsAssembly(typeof(CatalogDbContext).Assembly.FullName)));

        services.AddScoped<IGameRepository, GameRepository>();
        services.AddScoped<IUserLibraryRepository, UserLibraryRepository>();

        services.AddCatalogSearch(configuration);

        return services;
    }

    /// <summary>
    /// Elasticsearch (cliente oficial Elastic.Clients.Elasticsearch) + cache do catálogo.
    /// Credenciais (ApiKey ou usuário/senha) chegam por variáveis de ambiente vindas do Secret.
    /// </summary>
    public static IServiceCollection AddCatalogSearch(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ElasticsearchOptions>(configuration.GetSection(ElasticsearchOptions.SectionName));

        services.AddSingleton(sp =>
        {
            var opt = sp.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;
            var settings = new ElasticsearchClientSettings(new Uri(opt.Uri))
                .RequestTimeout(TimeSpan.FromSeconds(10));

            if (!string.IsNullOrWhiteSpace(opt.ApiKey))
                settings.Authentication(new ApiKey(opt.ApiKey));
            else if (!string.IsNullOrWhiteSpace(opt.Username))
                settings.Authentication(new BasicAuthentication(opt.Username, opt.Password ?? string.Empty));

            return new ElasticsearchClient(settings);
        });

        services.AddScoped<IGameSearchIndex, ElasticsearchGameSearchIndex>();
        services.AddHostedService<SearchIndexInitializer>();

        var cacheOptions = new CatalogCacheOptions();
        configuration.GetSection("Cache").Bind(cacheOptions);
        services.AddSingleton(cacheOptions);
        services.AddSingleton<CatalogCache>();

        return services;
    }
}
