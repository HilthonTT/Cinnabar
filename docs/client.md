# Client guide

`Cinnabar.Client` is an async .NET client for Cinnabar and any other server that speaks
RESP2, including Redis itself. It pools connections, so a single `RedisClient` can be
shared by the whole application.

## Creating a client

```csharp
using Cinnabar.Client;

// Host and port
await using var client = new RedisClient("localhost", 6379);

// Options
await using var configured = new RedisClient(new RedisClientOptions
{
    Host = "cache.internal",
    Port = 6379,
    Password = "s3cret",                         // sends AUTH on every new connection
    Username = "default",
    PoolSize = 20,                               // max concurrent commands
    PoolTimeout = TimeSpan.FromSeconds(5),       // wait for a free connection
    ConnectTimeout = TimeSpan.FromSeconds(5),
});

// Connection string: host:port, password@host:port, user:password@host:port, optional redis://
var fromString = new RedisClientOptions { ConnectionString = "redis://default:s3cret@cache.internal:6379" };
```

Reading `ConnectionString` back masks the password (`default:***@host:port`), so it is safe to log.

### Dependency injection

```csharp
using Cinnabar.Client.DependencyInjection;

builder.Services.AddRedisClient(builder.Configuration.GetConnectionString("Cinnabar")!);
// or
builder.Services.AddRedisClient(options => options.Host = "cinnabar");

// Inject RedisClient anywhere; it is registered as a singleton.
public sealed class CartService(RedisClient redis)
{
    public Task<long> AddAsync(string cartId, string item) => redis.RPushAsync($"cart:{cartId}", item);
}
```

## Commands

| Area | Methods |
|---|---|
| Server | `PingAsync`, `EchoAsync`, `InfoAsync`, `DbSizeAsync`, `FlushDbAsync`, `ConfigGetAsync` |
| Keys | `DeleteAsync`, `ExistsAsync`, `ExpireAsync`, `PersistAsync`, `TimeToLiveAsync`, `TypeAsync`, `KeysAsync`, `ScanAsync` |
| Strings | `GetAsync`, `SetAsync` (optionally with expiry), `SetIfNotExistsAsync`, `MGetAsync`, `MSetAsync`, `MSetIfNotExistsAsync`, `StrLenAsync`, `IncrementAsync`, `DecrementAsync`, `IncrementByAsync`, `DecrementByAsync` |
| Lists | `LPushAsync`, `RPushAsync`, `LPopAsync` (single or count), `RPopAsync`, `BLPopAsync`, `LLenAsync`, `LRangeAsync` |
| Hashes | `HSetAsync` (one field or many), `HSetIfNotExistsAsync`, `HGetAsync`, `HMGetAsync`, `HGetAllAsync`, `HKeysAsync`, `HValsAsync`, `HDelAsync`, `HExistsAsync`, `HLenAsync`, `HIncrByAsync`, `HIncrByFloatAsync` |
| Sets | `SAddAsync`, `SRemAsync`, `SIsMemberAsync`, `SMembersAsync`, `SCardAsync`, `SPopAsync`, `SMoveAsync`, `SInterAsync`, `SUnionAsync`, `SDiffAsync` |
| Sorted sets | `ZAddAsync` (one member or many), `ZIncrByAsync`, `ZScoreAsync`, `ZRankAsync`, `ZCardAsync`, `ZCountAsync`, `ZRemAsync`, `ZRangeAsync`, `ZRangeByScoreAsync`, `ZPopAsync` |
| Streams | `XAddAsync`, `XLenAsync`, `XRangeAsync` |
| Pub/Sub | `PublishAsync`, `SubscribeAsync` |
| Transactions | `ExecuteTransactionAsync` (plain, or optimistic with `WATCH`) |
| Anything else | `ExecuteAsync(params string[])` returns the raw `RespValue` |

```csharp
await client.SetAsync("user:1:name", "Ada");
await client.SetAsync("otp:1", "829114", TimeSpan.FromMinutes(5));
TimeSpan? ttl = await client.TimeToLiveAsync("otp:1");

if (await client.SetIfNotExistsAsync("lock:invoice:7", Environment.MachineName, TimeSpan.FromSeconds(30)))
{
    // we hold the lock
}

RespValue raw = await client.ExecuteAsync("LRANGE", "jobs", "0", "-1");
List<string?> items = raw.AsStringList();
```

Consumer groups (`XGROUP`, `XREADGROUP`, `XACK`, ...) and the less common commands have no
typed method yet; use `ExecuteAsync`.

## Hashes, sets and sorted sets

```csharp
await client.HSetAsync("user:1", [new("name", "Ada"), new("visits", "0")]);
await client.HIncrByAsync("user:1", "visits", 1);
Dictionary<string, string> user = await client.HGetAllAsync("user:1");

await client.SAddAsync("online", "ada", "bob");
bool online = await client.SIsMemberAsync("online", "ada");

await client.ZAddAsync("scores", [new("ada", 120), new("bob", 95)]);
await client.ZIncrByAsync("scores", "bob", 40);
List<RedisSortedSetEntry> top3 = await client.ZRangeAsync("scores", 0, 2, descending: true);
List<RedisSortedSetEntry> passed = await client.ZRangeByScoreAsync("scores", min: 100);
long? rank = await client.ZRankAsync("scores", "ada", descending: true);
```

Scores are `double`s; `double.PositiveInfinity` and `double.NegativeInfinity` map to
`+inf` and `-inf`.

## Scanning keys

`ScanAsync` pages through the keyspace with `SCAN`, so it never blocks the server with one
huge reply the way `KeysAsync` can:

```csharp
await foreach (string key in client.ScanAsync(pattern: "session:*", type: "hash", pageSize: 500))
{
    // A key that exists for the whole loop is returned exactly once.
}
```

## Blocking pops

```csharp
// Wait up to 10 seconds on two queues
(string Key, string Value)? job = await client.BLPopAsync(["jobs:high", "jobs:low"], TimeSpan.FromSeconds(10));
if (job is { } j)
{
    Console.WriteLine($"{j.Value} from {j.Key}");
}

// Wait forever, but stop when the app shuts down
var next = await client.BLPopAsync(["jobs"], timeout: null, cancellationToken: stoppingToken);
```

A blocking call holds a pooled connection for as long as it waits. Size `PoolSize` for the
number of concurrent waiters plus normal traffic.

## Streams

```csharp
string id = await client.XAddAsync("orders", [new("id", "1001"), new("total", "42.50")]);

foreach (RedisStreamEntry entry in await client.XRangeAsync("orders", start: "-", end: "+", count: 100))
{
    Console.WriteLine($"{entry.Id}: {string.Join(", ", entry.Fields.Select(f => $"{f.Key}={f.Value}"))}");
}
```

## Pub/Sub

`SubscribeAsync` opens a dedicated connection (a subscribed connection can't run other
commands) and returns once the server has confirmed the subscription, so a message
published right afterwards is not missed.

```csharp
await using RedisSubscriber subscriber = await client.SubscribeAsync("orders", "payments");

_ = Task.Run(async () =>
{
    await foreach (RedisMessage message in subscriber.Messages(stoppingToken))
    {
        Console.WriteLine($"[{message.Channel}] {message.Message}");
    }
});

await subscriber.SubscribeAsync("refunds");   // add channels later
await subscriber.UnsubscribeAsync("payments"); // or remove them
```

Disposing the subscriber closes its connection, which ends every subscription.

## Transactions

`MULTI` and `EXEC` must be sent on the same connection, so the client runs the whole
transaction on one pooled connection:

```csharp
IReadOnlyList<RespValue> results = await client.ExecuteTransactionAsync(
[
    ["INCR", "page:views"],
    ["EXPIRE", "page:views", "3600"],
]);

long views = results[0].AsLong();
```

Each element is that command's reply, which may be an error (check `IsError` or call
`ThrowIfError()`). If the server aborts the transaction (`EXECABORT`), a `RedisException`
is thrown. Against Cinnabar, transactions are isolated: no other client's command runs in the
middle of one.

### Optimistic locking with WATCH

To read a value and write something based on it without anyone changing it in between,
pass the keys to watch and a callback. The callback gets a `read` function that runs
commands on the transaction's connection, and returns the commands to queue:

```csharp
IReadOnlyList<RespValue>? result;
do
{
    result = await client.ExecuteTransactionAsync(["account:7"], async read =>
    {
        long balance = long.Parse((await read(["HGET", "account:7", "balance"])).AsString() ?? "0");
        if (balance < 10)
        {
            return [];  // nothing to do
        }

        return [["HINCRBY", "account:7", "balance", "-10"], ["RPUSH", "ledger:7", "-10"]];
    });
}
while (result is null);  // a watched key changed before EXEC: nothing was written, try again
```

The result is `null` only when a watched key was modified (by anyone) between `WATCH` and
`EXEC`. If the callback throws, the client sends `UNWATCH` before returning the connection
to the pool.

## Errors

| Exception | Meaning | Connection |
|---|---|---|
| `RedisException` | The server replied with an error. `ErrorCode` holds the prefix (`ERR`, `WRONGTYPE`, `NOAUTH`, `WRONGPASS`, `READONLY`, …). | Reused |
| `RedisConnectionException` | Connect failed or timed out, the pool was exhausted, the connection dropped, or the reply was not valid RESP. | Discarded |
| `OperationCanceledException` | Your cancellation token fired. | Discarded |

A connection is only returned to the pool after a complete request/reply exchange. If a
command is cancelled or fails mid-flight, its connection is closed, so a late reply can
never be delivered to the next caller.

## Thread safety and pooling

- `RedisClient` is thread-safe; share one instance.
- Each command borrows a connection, uses it, and returns it. Connections are opened
  lazily, up to `PoolSize`.
- If all connections are busy for longer than `PoolTimeout`, the command fails with
  `RedisConnectionException`.
