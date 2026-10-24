# What's Building MCP Server

A read-only [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server that exposes the
City of Nanaimo **What's Building** public web app (<https://www.nanaimo.ca/whatsbuilding/>) —
development applications, building permits and subdivisions — to AI clients.

Public access only: the server talks to What's Building over its public HTTPS endpoints,
anonymously. It never touches the database, uses no credentials, never calls any `Identity/*`
route, and exposes nothing user-specific. All tools are read-only.

> City of Nanaimo public data; may lag the city's systems; includes inactive and
> completed folders by default (set includeInactive=false for active folders only);
> not legal advice.

## Architecture

The upstream app is a legacy ASP.NET MVC 5 app, not a JSON API, so the server uses a
**cache-and-filter-locally** design:

- `SnapshotService` (`IHostedService` + `PeriodicTimer`, lazy load on first use) downloads
  `whatsbuilding.csv` twice every `CacheMinutes` (default 30): the default active set via
  GET, and the all-folders set (active plus inactive/completed) via a search-form POST with
  `SearchCriteria.ShowInactiveFolders=true`. It serves both as immutable in-memory snapshots.
  Failed refreshes keep serving the previous snapshots, flagged stale with a warning; every
  tool result carries `snapshotAgeSeconds`. Tools search the all-folders snapshot by default;
  `includeInactive=false` restricts a call to active folders only. When the all-folders
  snapshot is unavailable, calls fall back to the active snapshot with a warning.
- Search, filter, sort and paging run in memory over the snapshot (case-insensitive contains,
  matching upstream semantics). Nearby search uses the haversine formula.
- `DetailService` enriches `get_application` with the parsed `/FolderView/{fileNumber}` page
  (cached per file, short TTL, de-duplicated).
- `ReferenceDataService` scrapes the home-page search form once for type/area ids and a
  replayable field set (cached 24 h, with a built-in fallback table).
- Advanced path (optional): `get_recent_activity` also POSTs the complete scraped form to
  `/GetMarkers` with `ShowInactiveFolders=true` and a status-change window, so inactive and
  completed applications in the window are included. If that path fails, the tool falls back
  to snapshot dates with a clear warning. With `includeInactive=false` the tool searches the
  active snapshot only and skips the upstream window (which is inherently inactive-inclusive).

Good-citizen behavior: typed `HttpClient` via `HttpClientFactory` with a descriptive
`User-Agent`, Polly retry + timeout (`Microsoft.Extensions.Http.Resilience`), `IMemoryCache`
everywhere, in-flight request de-duplication, ASP.NET rate limiting on `/mcp`, and strict
response size limits. Outbound traffic is restricted to the configured host; absolute URLs
from callers are never fetched (SSRF prevention).

## Endpoints

| Endpoint  | Description                                                        |
|-----------|--------------------------------------------------------------------|
| `/mcp`    | MCP Streamable HTTP endpoint (tools, resources, prompts)           |
| `/health` | Health probe: `{status, recordCount, snapshotAgeSeconds, ...}`     |

Optional bearer protection: set `Mcp__ApiKey`; `/mcp` then requires
`Authorization: Bearer <key>` (constant-time comparison). `/health` stays open.

## Tools, resources, prompts

Tools: `search_applications`, `get_application`, `find_applications_near`,
`find_applications_by_address`, `list_neighbourhoods`, `list_application_types`,
`get_pending_permits`, `get_recent_activity`, `get_statistics`, `get_application_kml`.

Resources: `whatsbuilding://about`, `whatsbuilding://application/{fileNumber}`.

Prompts: `summarize_neighbourhood_activity` (neighbourhood, days).

List tools return `{total, returned, offset, snapshotAgeSeconds, items[], warning?}`
(results default 25, max 100).

## Configuration

| Setting | Env var | Default |
|---|---|---|
| `WhatsBuilding:BaseUrl` | `WhatsBuilding__BaseUrl` | `https://www.nanaimo.ca/whatsbuilding/` |
| `WhatsBuilding:CacheMinutes` | `WhatsBuilding__CacheMinutes` | `30` |
| `WhatsBuilding:HttpTimeoutSeconds` | `WhatsBuilding__HttpTimeoutSeconds` | `60` |
| `WhatsBuilding:UserAgent` | `WhatsBuilding__UserAgent` | `WhatsBuildingMcp/1.0 (+https://www.nanaimo.ca/whatsbuilding/)` |
| `WhatsBuilding:DetailCacheMinutes` | `WhatsBuilding__DetailCacheMinutes` | `15` |
| `WhatsBuilding:MaxCsvBytes` | `WhatsBuilding__MaxCsvBytes` | `67108864` (64 MB) |
| `WhatsBuilding:MaxResponseBytes` | `WhatsBuilding__MaxResponseBytes` | `10485760` (10 MB) |
| `WhatsBuilding:ReferenceDataCacheHours` | `WhatsBuilding__ReferenceDataCacheHours` | `24` |
| `WhatsBuilding:RecentActivityCacheMinutes` | `WhatsBuilding__RecentActivityCacheMinutes` | `15` |
| `Mcp:ApiKey` | `Mcp__ApiKey` | _(empty = open)_ |
| `Mcp:AllowedOrigins` | `Mcp__AllowedOrigins__0`, `__1`, … | _(empty = CORS off)_ |
| `RateLimit:PermitLimit` | `RateLimit__PermitLimit` | `60` |
| `RateLimit:WindowSeconds` | `RateLimit__WindowSeconds` | `60` |
| `RateLimit:QueueLimit` | `RateLimit__QueueLimit` | `10` |

## Run with Docker Compose

```bash
cp .env.example .env
# edit .env (set MCP_API_KEY, etc.)
docker compose up -d --build
curl http://localhost:8080/health
```

The container runs as a non-root user, listens on 8080, ships a `/health` healthcheck,
a restart policy, resource limits and a read-only filesystem.

## Run from source

Requires the .NET 10 SDK.

```bash
dotnet build WhatsBuildingMcp.sln
dotnet test WhatsBuildingMcp.sln
cd src/WhatsBuildingMcp
Mcp__ApiKey=secret dotnet run   # then open http://localhost:5235/health (see Properties/launchSettings.json)
```

## Example MCP client config

```json
{
  "type": "http",
  "url": "https://host/mcp",
  "headers": {
    "Authorization": "Bearer …"
  }
}
```

With the MCP Inspector:

```bash
npx @modelcontextprotocol/inspector --transport http --server-url http://localhost:8080/mcp
```

## Project layout

```text
WhatsBuildingMcp.sln
src/WhatsBuildingMcp/          # ASP.NET Core web + MCP server (Streamable HTTP)
  Tools/                       # McpServerToolType / ResourceType / PromptType
  Services/                    # WhatsBuildingClient, SnapshotService, parsers, query engine
  Models/                      # Records and DTOs
  Options/                     # WhatsBuilding / Mcp / RateLimit options
  Middleware/                  # Optional API-key guard for /mcp
tests/WhatsBuildingMcp.Tests/  # xUnit: CSV, query, geo, HTML fixtures, tools, middleware
```

## Upstream notes (verified against the live site)

- `whatsbuilding.csv` header ends with a trailing comma; dates look like `9/18/2012 12:00:00 AM`.
- POSTing the complete search form to `whatsbuilding.csv` with
  `SearchCriteria.ShowInactiveFolders=true` (prepended ahead of the hidden `false`, as for
  `/GetMarkers`) returns the all-folders CSV. A GET query string such as
  `?SearchCriteria.ShowInactiveFolders=true` is ignored upstream — verified byte-identical
  to the default active set — so the form POST is required for inactive folders.
- Unknown file numbers return HTTP 200 with `The application was not found.` on `/FolderView/`.
- Notices live under an `Important Dates & Notices` heading; no per-application educational
  note was observed in Folder pages, so `educationalNote` is captured opportunistically
  (null when absent).
