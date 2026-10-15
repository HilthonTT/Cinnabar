# Architecture

This document explains how Cinnabar is put together: the projects, how a command travels
through the server, and the design decisions behind concurrency, blocking commands,
replication and persistence.

## Projects and dependencies

```
                 ┌────────────────────┐
                 │  Cinnabar.Protocol │   RESP2 reader + encoder (no dependencies)
                 └─────────▲──────────┘
             ┌─────────────┴─────────────┐
┌────────────┴───────┐           ┌───────┴───────────┐
│  Cinnabar.Server   │           │  Cinnabar.Client  │
│  (executable)      │           │  (library)        │
└────────────┬───────┘           └───────────────────┘
             │
┌────────────▼───────┐
│  Cinnabar.Storage  │   Keyspace + RDB persistence (no networking)
└────────────────────┘
```

| Project | Responsibility | Key types |
|---|---|---|
| `Cinnabar.Protocol` | Parse and encode RESP2. Handles values split across TCP reads, pipelining, inline commands, and size/depth limits for untrusted input. | `RespReader`, `RespEncoder`, `RespValue` |
| `Cinnabar.Storage` | The data. A single keyspace holding strings, lists and streams, with expiry and RDB snapshots. Thread-safe and independent of the network layer. | `Keyspace`, `StreamId`, `KeyPattern`, `RdbReader`, `RdbWriter` |
| `Cinnabar.Server` | Networking, command dispatch, pub/sub, blocking commands, replication, persistence scheduling, security, metrics. | `CinnabarServer`, `CommandDispatcher`, `ClientSession`, `ReplicationMaster`, `ReplicationClient` |
| `Cinnabar.Client` | Pooled async client for applications. | `RedisClient`, `RedisSubscriber` |

## Life of a command

```
 socket ──► ConnectionHandler ──► RespReader ──► Command ──► CommandDispatcher ──► handler ──► Keyspace
                    ▲                                              │
                    └────────────── Reply (pre-encoded RESP) ◄─────┘
                                                                   └──► ReplicationMaster.Propagate (writes)
```

1. **Accept.** `TcpServer` accepts a socket, asks `IpGuard` whether the IP is under its
   connection limits, and creates a `ClientSession`.
2. **Read.** `ConnectionHandler` reads from a `PipeReader` with `RespReader`. Partial
   reads simply wait for more bytes; pipelined commands are processed one after another.
   Malformed input gets `-ERR Protocol error` and the connection is closed, because a RESP
   stream cannot be resynchronised.
3. **Rate limit.** Each command spends a token from the IP's `TokenBucketLimiter`.
4. **Dispatch.** `CommandDispatcher` looks the name up (case-insensitively) and applies
   the rules every command shares, in order:
   unknown command → not authenticated (`NOAUTH`) → wrong argument count →
   not allowed in subscribed mode → *queue it* if the session is inside `MULTI` →
   write on a replica (`READONLY`).
5. **Execute.** The handler runs. A `StorageException` (for example `WRONGTYPE`) becomes an
   error reply; any other exception becomes `-ERR ...` and is logged without killing the
   connection.
6. **Propagate.** On a master, a successful write is sent to every replica (see below),
   and the session remembers the replication offset for `WAIT`.
7. **Reply.** The pre-encoded `Reply` bytes are written through `ClientSession.SendAsync`.

### Command handlers

Each command is a small class implementing `ICommandHandler` and carrying a `[Command]`
attribute:

```csharp
[Command("LLEN", MinArgs = 1, MaxArgs = 1, Group = "list", Syntax = "key")]
internal sealed class LLenHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.ListLength(command[0])));
}
```

Handlers are discovered by reflection at startup and registered as singletons, so adding
a command means adding one class. The attribute's `Flags` drive the dispatcher:

| Flag | Meaning |
|---|---|
| `Write` | Rejected on replicas; propagated to replicas on a master. |
| `Blocking` | May wait (BLPOP, XREAD BLOCK, WAIT). Blocking writes propagate their own effect. |
| `PubSub` | Allowed in subscribed mode. |
| `NoAuth` | Allowed before `AUTH`. |
| `Transaction` | Runs immediately inside `MULTI` instead of being queued. |

A handler can change what is replicated with `Reply.WithPropagation(...)`. `XADD *` uses this
to send the generated ID, and `SET ... EX` sends an absolute `PXAT`, so replicas store
identical data instead of re-running time- or ID-dependent logic.

## Storage and concurrency

`Keyspace` keeps **one** `ConcurrentDictionary<string, Entry>` for all types. An entry
holds the value (a `string`, a `LinkedList<string>` or a `StreamLog`) and an optional
absolute expiry. Because there is a single map, a key can never hold two types at once:
`SET` replaces a list, and `LPUSH` on a string raises `WRONGTYPE`.

**Locking.** Every key hashes to one of 256 lock stripes, and every operation holds its
key's stripe for its whole duration. That makes each single-key operation atomic (for
example `IncrementBy` or "pop and delete the list when it becomes empty") while operations
on different keys run in parallel. No operation ever holds two stripes, so there are no
lock-ordering deadlocks.

**Expiry** is stored as Unix milliseconds and read through an injectable `TimeProvider`,
so tests can move a fake clock instead of sleeping. Expired keys are invisible immediately
(every read checks), and are removed lazily on access and by a sweep once a second.

**Streams** (`StreamLog`) keep entries in a list sorted by `StreamId`, so ranges use a binary
search. `XADD *` uses `max(now, last-ms)` so IDs keep increasing even if the system clock
moves backwards.

## Blocking commands

`BLPOP`, `BRPOP` and `XREAD BLOCK` use `KeyNotifier`. The order of operations is what makes
them correct:

1. **Register** interest in the keys.
2. **Check** for data; return it if present.
3. **Wait** on the registration until it is signalled, the timeout passes, or the client disconnects.

Registering *before* checking closes the lost-wake-up window: a push that lands between
steps 1 and 2 is seen in step 2, and one after step 2 completes the wait. Every waiter on a
key is woken; the pop itself is atomic in the keyspace, so exactly one waiter gets each
element and the rest go back to waiting.

While a command blocks, the connection loop isn't reading, so `ClientSession.WatchForDisconnect`
polls the socket (every 100 ms) to detect a client that hung up. That client's waiter is
removed, so it can't swallow a later push.

Inside `EXEC`, blocking commands don't block, as in Redis.

## Pub/Sub

`PubSubBroker` maps channels to subscribed sessions. `PUBLISH` encodes the
`["message", channel, payload]` push once and queues it on each subscriber's session.
Each session drains its queue on a background loop, so a slow subscriber never blocks the
publisher. All writes to a socket (replies, pushes, replication traffic) go through
`ClientSession.SendAsync`, which serialises them so frames never interleave.

## Replication

```
 replica                                   master
   │  AUTH (if masterauth), PING              │
   │  REPLCONF listening-port <port>          │
   │  REPLCONF capa psync2                    │
   │  PSYNC ? -1  ─────────────────────────►  │  WriteBarrier: wait for in-flight writes,
   │                                          │  snapshot keyspace + read offset, register replica
   │  ◄──────── +FULLRESYNC <replid> <offset> │
   │  ◄──────── $<len>\r\n<RDB bytes>         │
   │  ◄──────── write commands …              │  every propagated write, in order
   │  REPLCONF ACK <offset> ────────────────► │  (in reply to REPLCONF GETACK *)
```

**Master (`ReplicationMaster`).** The offset is the total number of bytes of commands
propagated. `Propagate` appends a command to every replica's queue under one lock, so all
replicas see the same order. Each `ConnectedReplica` drains its queue on its own loop, so a
slow replica doesn't block writers.

**Consistent snapshots (`WriteBarrier`).** A full sync needs the snapshot and the offset to
describe the same moment. Write commands hold the barrier in *shared* mode from execution
until propagation; a snapshot takes it *exclusively*, waiting for in-flight writes to drain.
Without this, a write landing during the snapshot could end up both in the snapshot and in
the stream (applied twice on the replica), or in neither.

**WAIT** sends `REPLCONF GETACK *` *through the replication stream*, so a replica answers
only after applying everything sent before it, and then polls the acknowledged offsets.

**Replica (`ReplicationClient`).** It runs the handshake, loads the RDB into an emptied
keyspace, and applies the stream through the normal dispatcher using a special
replication-link session (the only session allowed to write on a read-only replica). It
tracks the master's offset by counting the bytes of each frame, answers `GETACK`, and
reconnects with exponential back-off (up to 5 s) if the link drops.

## Persistence

`SnapshotService` drives the storage library's `RdbReader`/`RdbWriter`:

- **Load on startup.** If the file exists but can't be read, startup **fails** on purpose:
  starting empty and saving on shutdown would overwrite the user's data.
- **Save** on `SAVE`, `BGSAVE`, every `SaveIntervalSeconds`, and on graceful shutdown.
  Writes go to `dump.rdb.tmp` first and then replace the file, so a crash never leaves a
  half-written snapshot.
- **Format.** RDB version 9 with a CRC-64/Jones checksum. Strings are type 0 and lists are
  type 1 (the plain list encoding, which Redis can still load). Streams are skipped, with a
  warning. The reader also understands integer-encoded and LZF-compressed strings, so it can
  load string data from real Redis dumps.

## Security and limits

- `requirepass` enables authentication; `AUTH` compares SHA-256 digests in constant time.
- `IpGuard` enforces `MaxConnections` and `MaxConnectionsPerIp`, and gives each IP a token
  bucket (`RateLimitPerSecond`, `RateLimitBurst`). Rate-limit state lives for the process
  lifetime, so reconnecting doesn't reset it.
- `RespReader` limits bulk length (512 MB), array length, line length and nesting depth, so
  a client can't make the server allocate unbounded memory with a single header.

## Observability

- Console logs via `Microsoft.Extensions.Logging`. Per-command logging is at `Trace`, so it
  costs nothing at the default `Information` level.
- Prometheus metrics on `--metrics-port` (default 9090): connections, rejected connections,
  commands by name and outcome, latency histogram, rate-limited commands, key count,
  connected replicas and replication offset.
- `INFO` reports server, client, persistence, replication and keyspace sections.
