# FCG Catalog API (Fase 4)

Microsserviço de catálogo de jogos e fluxo de compra da FIAP Cloud Games.
Guia central de infraestrutura, pipeline e demonstração: **fcg-orchestration** (README).

## O que a Fase 4 adicionou aqui

| Requisito | Implementação |
|---|---|
| **Busca avançada** (Elasticsearch) | `GET /api/games/search?q=...` com **Fuzzy Search** e ordenação por **relevância** (`_score`) |
| **Sincronização do índice** | Criar/editar/promover/desativar um jogo grava no SQL Server **e** reindexa no Elasticsearch (`GameService.SyncAsync`) |
| **Reindex automático** | `SearchIndexInitializer` cria o índice e reindexa o catálogo na subida da API se o índice divergir do banco |
| **Cache distribuído** (Redis) | `GET /api/games` e `/search` cacheados via `IDistributedCache` (StackExchange.Redis); invalidação por token de versão a cada escrita |
| **NoSQL** (MongoDB) | Avaliações dos jogos (`MongoDB.Driver`), com resumo de nota cacheado no Redis |
| **Zero credenciais no código** | Nenhuma senha em `appsettings*.json`/YAML; tudo por variável de ambiente vinda do Secret |
| **Testes unitários** | `tests/FCG.Catalog.Tests` (xUnit + Moq) executados na pipeline |

### Como a busca funciona

```
GET /api/games/search?q=wiitcher&genre=RPG&page=1&size=10
```

- `multi_match` em `title^3`, `genre^2`, `description` com `fuzziness: AUTO` (tolera erros de digitação)
- `match_phrase_prefix` no título (boost 4) para digitação parcial (`witc` → *The Witcher 3*)
- Filtros: `isActive = true` e gênero (opcional, exato)
- Ordenação: `_score` decrescente, desempate pelo mais recente
- Resposta traz `score` por resultado e os cabeçalhos `X-Cache: HIT|MISS` e `X-Elapsed-Ms`

O SQL Server continua sendo a **fonte da verdade**: se o Elasticsearch estiver fora do ar, a escrita **não falha** (o erro é logado) e o reindex da próxima subida recompõe o índice. Se o Redis cair, a API consulta direto a fonte.

## Endpoints

| Método | Rota | Descrição | Auth |
|---|---|---|---|
| GET | `/api/games` | Lista jogos (cache Redis) | Não |
| **GET** | **`/api/games/search`** | **Busca fuzzy por relevância (Elasticsearch)** | Não |
| GET | `/api/games/{id}` | Busca jogo por ID | Não |
| GET | `/api/games/genre/{genre}` | Filtra por gênero | Não |
| POST | `/api/games` | Cadastra jogo (indexa) | Admin |
| PUT | `/api/games/{id}` | Atualiza jogo (reindexa) | Admin |
| PATCH | `/api/games/{id}/promotion` | Aplica promoção (reindexa) | Admin |
| DELETE | `/api/games/{id}/promotion` | Remove promoção (reindexa) | Admin |
| DELETE | `/api/games/{id}` | Desativa jogo (reindexa) | Admin |
| POST | `/api/games/{id}/purchase` | Inicia compra | Autenticado |
| GET/POST | `/api/games/{id}/reviews` | Avaliações (MongoDB) | Misto |
| GET | `/health` · `/metrics` | Saúde · Prometheus | Não |

## Configuração (todas por variável de ambiente)

| Variável | Origem em produção | Descrição |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | montada no pod a partir de `SQL_SA_PASSWORD` (Secret) | SQL Server |
| `ConnectionStrings__MongoDB` | montada a partir de `MONGO_USER/PASSWORD` (Secret) | MongoDB |
| `ConnectionStrings__Redis` | montada a partir de `REDIS_PASSWORD` (Secret) | Redis (`host:6379,password=...`) |
| `Jwt__Secret` | Secret (Key Vault) | Chave JWT (a mesma do UsersAPI) |
| `RabbitMQ__Host/Username/Password` | ConfigMap + Secret | Mensageria |
| `Elasticsearch__Uri` / `__IndexName` | ConfigMap | Endpoint e índice |
| `Elasticsearch__ApiKey` **ou** `__Username` + `__Password` | Secret | Elastic Cloud (API key) ou basic auth |
| `Cache__ListTtlSeconds` / `__SearchTtlSeconds` | ConfigMap (opcional) | TTL do cache (padrão 300 / 120) |

Sem `Jwt__Secret`, `MongoDB`, `Redis` ou credenciais do RabbitMQ a API **recusa subir** (não existe valor padrão com senha).

## Rodar e testar

```bash
# stack completa local (raiz do workspace/orquestração)
bash scripts/generate-local-env.sh && docker compose up -d --build

# testes unitários
dotnet test tests/FCG.Catalog.Tests -c Release
```

## CI/CD

`.github/workflows/ci-cd.yml`: build → testes → imagem Docker versionada (`1.0.<run>`, `sha-<commit>`, `latest`) → scan Trivy → push no ACR → deploy no AKS com Rolling Update (`k8s/deployment.yaml`, `maxUnavailable: 0`) e rollback automático se o rollout falhar.
