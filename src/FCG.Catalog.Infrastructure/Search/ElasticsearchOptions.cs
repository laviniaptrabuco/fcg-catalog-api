namespace FCG.Catalog.Infrastructure.Search;

/// <summary>
/// Configuração do Elasticsearch. Uri/IndexName vêm do ConfigMap; ApiKey (Elastic Cloud) ou
/// Username/Password (Elasticsearch/OpenSearch com basic auth) vêm de Secret/Key Vault em runtime.
/// Nada disso tem valor padrão sensível no código-fonte.
/// </summary>
public class ElasticsearchOptions
{
    public const string SectionName = "Elasticsearch";

    public string Uri { get; set; } = "http://localhost:9200";
    public string IndexName { get; set; } = "fcg-games";
    public string? ApiKey { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}
