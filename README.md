<p align="center">
  <img src="docs/assets/logo.svg" width="128" height="128" alt="Cinnabar logo">
</p>

<h1 align="center">Cinnabar</h1>

<p align="center">
  A Redis-compatible in-memory data store written from scratch in C# / .NET 10,<br>
  with a pooled async client library.
</p>

<p align="center">
  <a href="https://github.com/HilthonTT/redis-clone/actions/workflows/ci.yml"><img src="https://github.com/HilthonTT/redis-clone/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
</p>

---

Cinnabar speaks RESP2, the Redis wire protocol, so `redis-cli` and existing Redis client
libraries can talk to it. It implements strings, lists, streams, pub/sub, transactions,
key expiry, master–replica replication and RDB snapshots. It is a learning project that
takes protocol parsing, concurrency, replication and persistence seriously, not a drop-in
production replacement for Redis. See [Limitations](#limitations).

> **Why "Cinnabar"?** Cinnabar is the bright red mineral that gave vermilion its colour: a
> nod to Redis (the red database) without borrowing its name.

## Contents

- [Quick start](#quick-start)
- [Features](#features)
- [Using the client library](#using-the-client-library)
- [Project layout](#project-layout)
- [Building and testing](#building-and-testing)
- [Limitations](#limitations)
- [Documentation](#documentation)

## Quick start

**Prerequisites:** the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
Docker is optional.

```bash
# Run the server on port 6379
dotnet run --project src/Cinnabar.Server

# Talk to it with redis-cli, or even plain netcat (inline commands are supported)
redis-cli -p 6379 SET greeting "hello" EX 60
redis-cli -p 6379 GET greeting
printf 'PING\r\n' | nc localhost 6379
```

Common options (`--help` lists them all; see [docs/configuration.md](docs/configuration.md)):

```bash
dotnet run --project src/Cinnabar.Server -- --port 6380 --requirepass s3cret
dotnet run --project src/Cinnabar.Server -- --dir ./data --save-interval 60
dotnet run --project src/Cinnabar.Server -- --port 6380 --replicaof localhost 6379
dotnet run --project src/Cinnabar.Server -- --config cinnabar.json
```

With Docker Compose (server, plus a sample web API on <http://localhost:8080/swagger>):

```bash
docker compose up --build
```

## Features

| Area | What you get |
|------|--------------|
| **Data types** | Strings (`GET`, `SET` with `NX`/`XX`/`EX`/`PX`/`EXAT`/`PXAT`/`KEEPTTL`, `INCR`…), lists (`LPUSH`, `RPOP`, `LRANGE`, blocking `BLPOP`/`BRPOP`…), streams (`XADD`, `XRANGE`, blocking `XREAD`) |
| **Keys** | `DEL`, `EXISTS`, `TYPE`, `KEYS` with glob patterns, `EXPIRE`/`PEXPIRE`/`EXPIREAT`/`PEXPIREAT`, `PERSIST`, `TTL`/`PTTL` on every type |
| **Pub/Sub** | `SUBSCRIBE`, `UNSUBSCRIBE`, `PUBLISH` with fan-out to every subscriber |
| **Transactions** | `MULTI` / `EXEC` / `DISCARD`, with `EXECABORT` for commands rejected while queueing |
| **Replication** | `--replicaof` full sync (snapshot plus command stream), read-only replicas, `WAIT`, automatic reconnect |
| **Persistence** | RDB snapshots: load on start, `SAVE`/`BGSAVE`, periodic and on-shutdown saves, atomic writes, CRC-64 check; reads LZF-compressed Redis dumps |
| **Security** | `requirepass` + `AUTH [user] pass` (constant-time comparison), per-IP and total connection limits, per-IP command rate limiting |
| **Operations** | Prometheus metrics on `:9090/metrics`, `INFO`, `CONFIG GET`, structured console logging, graceful shutdown on Ctrl+C / SIGTERM |
| **Client** | Pooled async .NET client, dedicated-connection pub/sub as `IAsyncEnumerable`, transactions, DI registration |

The full command reference is in [docs/commands.md](docs/commands.md).

## Using the client library

```csharp
using Cinnabar.Client;

await using var client = new RedisClient("localhost", 6379);

// Strings and expiry
await client.SetAsync("session:42", "token", TimeSpan.FromMinutes(30));
string? token = await client.GetAsync("session:42");
bool acquired = await client.SetIfNotExistsAsync("lock:report", "worker-1", TimeSpan.FromSeconds(10));

// Lists and blocking pops
await client.RPushAsync("jobs", "resize", "upload");
(string Key, string Value)? job = await client.BLPopAsync(["jobs"], TimeSpan.FromSeconds(5));

// Streams
string id = await client.XAddAsync("events", [new("action", "click"), new("page", "/home")]);
List<RedisStreamEntry> recent = await client.XRangeAsync("events", count: 10);

// Transactions run on a single connection
var results = await client.ExecuteTransactionAsync([["INCR", "visits"], ["EXPIRE", "visits", "60"]]);

// Pub/Sub
await using var subscriber = await client.SubscribeAsync("news");
await client.PublishAsync("news", "hello");
await foreach (var message in subscriber.Messages())
{
    Console.WriteLine($"{message.Channel}: {message.Message}");
    break;
}
```

ASP.NET Core / Blazor registration:

```csharp
using Cinnabar.Client.DependencyInjection;

builder.Services.AddRedisClient("localhost:6379");            // or
builder.Services.AddRedisClient(o => { o.Host = "cinnabar"; o.PoolSize = 20; o.Password = "s3cret"; });
```

The client is plain RESP2, so it also works against a real Redis server. More in
[docs/client.md](docs/client.md).

## Project layout

```
Cinnabar.slnx
├── src/
│   ├── Cinnabar.Protocol/        RESP2 reader/encoder shared by server and client
│   ├── Cinnabar.Storage/         Keyspace (strings, lists, streams, expiry) + RDB persistence
│   ├── Cinnabar.Server/          The server executable
│   │   ├── Commands/             Dispatcher + one folder per command family
│   │   ├── Networking/           TCP listener, per-connection loop, client sessions
│   │   ├── Replication/          Master streaming, replica link, write barrier
│   │   ├── Persistence/          Snapshot load/save scheduling
│   │   ├── PubSub/  Blocking/    Pub/sub broker, wake-ups for BLPOP/XREAD
│   │   ├── Security/             Connection limits and rate limiting
│   │   ├── Configuration/        Settings model and command-line parsing
│   │   ├── Diagnostics/          Prometheus metrics
│   │   └── Hosting/              CinnabarServer lifecycle and DI wiring
│   └── Cinnabar.Client/          Pooled async client library
├── tests/                        One test project per library, plus end-to-end integration tests
├── samples/Cinnabar.Samples.WebApi/  Minimal API that exercises the client
└── docs/                         Architecture, commands, configuration, client guide
```

The dependency graph is one-directional: `Server → Storage, Protocol` and
`Client → Protocol`. Storage knows nothing about networking, which keeps it easy to test.
[docs/architecture.md](docs/architecture.md) explains how a command flows through the server.

## Building and testing

```bash
dotnet build Cinnabar.slnx
dotnet test Cinnabar.slnx
```

| Test project | Covers |
|---|---|
| `Cinnabar.Protocol.Tests` | RESP parsing across split reads, limits, inline commands, encoding |
| `Cinnabar.Storage.Tests` | Keyspace semantics, expiry with a fake clock, streams, glob matching, RDB round trips, concurrency |
| `Cinnabar.Server.Tests` | Every command through the dispatcher, auth, transactions, blocking, configuration, rate limiting |
| `Cinnabar.Client.Tests` | Connection strings, pooling and connection recovery against a fake server |
| `Cinnabar.IntegrationTests` | Real server over TCP: client API, pipelining, pub/sub, blocking, auth, replication, persistence |

## Limitations

Cinnabar deliberately implements a subset of Redis:

- **One database** (`SELECT 0` only) and no hashes, sets, sorted sets, Lua, ACL users or cluster mode.
- **Values are text.** Keys and values are stored as UTF-8 strings, so binary payloads are not byte-exact.
- **Transactions are not isolated.** Commands queued with `MULTI` run back to back, but other clients' commands can interleave, and there is no `WATCH`.
- **Persistence covers strings and lists.** Streams are kept in memory but not written to snapshots. Real Redis dumps load if they contain only strings (including LZF-compressed and integer-encoded ones).
- **Replication always does a full sync.** There is no partial resync backlog, and chained replicas are not supported.

## Documentation

- [Architecture](docs/architecture.md): components, request flow, concurrency, replication and persistence internals
- [Command reference](docs/commands.md): every supported command with its syntax and notes
- [Configuration](docs/configuration.md): command-line flags, JSON settings, Docker
- [Client guide](docs/client.md): the .NET client library in depth
- [Changelog](CHANGELOG.md): the rename from *redis-clone*, bug fixes and breaking changes

## License

[MIT](LICENSE)
