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
libraries can talk to it. It implements strings, lists, hashes, sets, sorted sets, streams
with consumer groups, pub/sub, isolated transactions with `WATCH`, `SCAN`, key expiry,
master–replica replication and RDB snapshots, and it is binary-safe. It is a learning project that
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
| **Data types** | Strings (`GET`, `SET` with `NX`/`XX`/`EX`/`PX`/`EXAT`/`PXAT`/`KEEPTTL`, `MGET`/`MSET`, `INCR`…), lists (`LPUSH`, `RPOP`, `LRANGE`, blocking `BLPOP`/`BRPOP`…), hashes (`HSET`, `HGETALL`, `HINCRBY`…), sets (`SADD`, `SINTER`, `SMOVE`, `SUNIONSTORE`…), sorted sets (`ZADD` with `NX`/`XX`/`GT`/`LT`/`CH`/`INCR`, `ZRANGE` by rank, score or lex, `ZRANK`, `ZPOPMIN`…), streams (`XADD`, `XRANGE`, blocking `XREAD`) |
| **Consumer groups** | `XGROUP`, blocking `XREADGROUP`, `XACK`, `XPENDING`, `XCLAIM`, `XAUTOCLAIM` |
| **Binary safety** | Keys, values, fields and members are stored byte for byte, including invalid UTF-8 |
| **Keys** | `DEL`, `EXISTS`, `TYPE`, `KEYS` with glob patterns, `SCAN`/`HSCAN`/`SSCAN`/`ZSCAN` cursors, `EXPIRE`/`PEXPIRE`/`EXPIREAT`/`PEXPIREAT`, `PERSIST`, `TTL`/`PTTL` on every type |
| **Pub/Sub** | `SUBSCRIBE`, `UNSUBSCRIBE`, `PUBLISH` with fan-out to every subscriber |
| **Transactions** | `MULTI` / `EXEC` / `DISCARD`, isolated from other clients; optimistic locking with `WATCH`/`UNWATCH`; `EXECABORT` for commands rejected while queueing |
| **Replication** | `--replicaof` full sync (snapshot plus command stream), read-only replicas, `WAIT`, automatic reconnect; randomised and time-dependent writes are replicated deterministically |
| **Persistence** | RDB snapshots of strings, lists, hashes, sets and sorted sets: load on start, `SAVE`/`BGSAVE`, periodic and on-shutdown saves, atomic writes, CRC-64 check; reads LZF-compressed Redis dumps |
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

// Hashes, sets and sorted sets
await client.HSetAsync("user:1", [new("name", "Ada"), new("city", "London")]);
Dictionary<string, string> user = await client.HGetAllAsync("user:1");
await client.SAddAsync("tags:post:7", "dotnet", "redis");
await client.ZAddAsync("leaderboard", [new("ada", 120), new("bob", 95)]);
List<RedisSortedSetEntry> top = await client.ZRangeAsync("leaderboard", 0, 9, descending: true);

// Streams
string id = await client.XAddAsync("events", [new("action", "click"), new("page", "/home")]);
List<RedisStreamEntry> recent = await client.XRangeAsync("events", count: 10);

// Iterate keys without blocking the server
await foreach (string key in client.ScanAsync("user:*")) { /* ... */ }

// Transactions run on a single connection and are isolated
var results = await client.ExecuteTransactionAsync([["INCR", "visits"], ["EXPIRE", "visits", "60"]]);

// Optimistic locking: null means a watched key changed, so retry
var outcome = await client.ExecuteTransactionAsync(["balance"], async read =>
{
    long balance = long.Parse((await read(["GET", "balance"])).AsString() ?? "0");
    return [["SET", "balance", (balance - 10).ToString()]];
});

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
│   ├── Cinnabar.Storage/         Keyspace (every data type, expiry, WATCH versions, SCAN) + RDB persistence
│   ├── Cinnabar.Server/          The server executable
│   │   ├── Commands/             Dispatcher + one folder per command family
│   │   ├── Concurrency/          Shared/exclusive gate behind EXEC isolation and the write barrier
│   │   ├── Networking/           TCP listener, per-connection loop, client sessions
│   │   ├── Replication/          Master streaming, replica link, write barrier
│   │   ├── Persistence/          Snapshot load/save scheduling
│   │   ├── PubSub/  Blocking/    Pub/sub broker, wake-ups for BLPOP/XREAD/XREADGROUP
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
| `Cinnabar.Protocol.Tests` | RESP parsing across split reads, limits, inline commands, UTF-8 and byte-exact encoding |
| `Cinnabar.Storage.Tests` | Keyspace semantics for every type, expiry with a fake clock, consumer groups, `WATCH` versions, `SCAN` guarantees, glob matching, RDB round trips, multi-key atomicity and concurrency |
| `Cinnabar.Server.Tests` | Every command through the dispatcher, auth, transactions, `WATCH` and `EXEC` isolation, blocking, replication rewrites, configuration, rate limiting |
| `Cinnabar.Client.Tests` | Connection strings, pooling and connection recovery against a fake server |
| `Cinnabar.IntegrationTests` | Real server over TCP: client API, binary payloads, pipelining, pub/sub, blocking, optimistic transactions under contention, auth, replication, persistence |

## Limitations

Cinnabar deliberately implements a subset of Redis:

- **One database** (`SELECT 0` only), and no Lua, ACL users, cluster mode, RESP3, AOF or `maxmemory` eviction.
- **Not every command.** Notable gaps: `XINFO`, `XDEL`, `XTRIM`, `ZUNIONSTORE`/`ZINTERSTORE`, blocking sorted-set pops, pattern subscriptions, `RENAME`, `CONFIG SET`. See the [command reference](docs/commands.md).
- **Persistence skips streams.** Streams and their consumer groups live in memory only, and a replica's full sync doesn't include them. Real Redis dumps load only if they avoid the compact encodings (listpacks, intsets) Redis uses for small collections.
- **Replication always does a full sync.** There is no partial resync backlog, and chained replicas are not supported. A transaction's writes are replicated one by one, so a replica can briefly show part of one.
- **Some operations are slower than Redis.** `SCAN` walks the whole keyspace on each call, and sorted-set rank lookups are O(rank), not O(log n).

## Documentation

- [Architecture](docs/architecture.md): components, request flow, concurrency, replication and persistence internals
- [Command reference](docs/commands.md): every supported command with its syntax and notes
- [Configuration](docs/configuration.md): command-line flags, JSON settings, Docker
- [Client guide](docs/client.md): the .NET client library in depth
- [Changelog](CHANGELOG.md): the rename from *redis-clone*, bug fixes and breaking changes

## License

[MIT](LICENSE)
