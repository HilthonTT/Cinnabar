# Architecture

This document explains how Cinnabar is put together: the projects, how a command travels
through the server, and the design decisions behind binary safety, concurrency,
transactions, blocking commands, replication and persistence.

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
| `Cinnabar.Protocol` | Parse and encode RESP2. Handles values split across TCP reads, pipelining, inline commands, and size/depth limits for untrusted input. Text decoding is configurable (UTF-8 for clients, byte-exact for the server). | `RespReader`, `RespEncoder`, `RespValue` |
| `Cinnabar.Storage` | The data. A single keyspace holding strings, lists, hashes, sets, sorted sets and streams (with consumer groups), with expiry, `WATCH` versions, `SCAN` cursors and RDB snapshots. Thread-safe and independent of the network layer. | `Keyspace`, `ByteString`, `StreamId`, `KeyPattern`, `RdbReader`, `RdbWriter` |
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
5. **Execute.** The handler runs while holding the transaction gate in shared mode (see
   [Transactions](#transactions-and-watch)). A `StorageException` (for example `WRONGTYPE`)
   becomes an error reply; any other exception becomes `-ERR ...` and is logged without
   killing the connection.
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
| `Blocking` | May wait (BLPOP, XREAD BLOCK, XREADGROUP BLOCK, WAIT). Takes the transaction gate itself, only while it looks at data. Blocking writes propagate their own effect. |
| `PubSub` | Allowed in subscribed mode. |
| `NoAuth` | Allowed before `AUTH`. |
| `Transaction` | Runs immediately inside `MULTI` instead of being queued (MULTI, EXEC, DISCARD, WATCH), and without the transaction gate. |
| `NoGate` | Runs without the transaction gate because it is long-running and needs no isolation (PSYNC). |

A handler can change what is replicated with `Reply.WithPropagation(...)`, so replicas store
identical data instead of re-running time-, ID- or randomness-dependent logic:

| Command | Replicated as |
|---|---|
| `XADD *` | `XADD` with the generated ID |
| `SET ... EX/PX` | `SET ... PXAT` with an absolute time |
| `SPOP` | `SREM` of the members actually removed |
| `HINCRBYFLOAT` | `HSET` with the resulting value |
| `XGROUP CREATE ... $` | `XGROUP CREATE` with the resolved ID |
| `XREADGROUP`, `XCLAIM`, `XAUTOCLAIM` | `XCLAIM key group consumer 0 <ids> TIME <ms> ... FORCE LASTID <id>` |

Writes that change nothing (`SADD` of an existing member, `DEL` of a missing key) are not
replicated at all.

## Binary safety

Redis keys and values are byte strings, not text. Cinnabar keeps them in ordinary .NET
`string`s, but with **one `char` per byte**: the server decodes RESP payloads as Latin-1
(`RespReaderOptions.Server`), which maps each byte 0–255 to the `char` with the same value,
and encodes replies, replication traffic and RDB files the same way. Any payload, including
invalid UTF-8, therefore round-trips exactly, `STRLEN` counts bytes, members compare and
sort byte by byte, and glob patterns match bytes, all as in Redis.

So a UTF-8 value such as `é` (bytes `C3 A9`) is stored as two chars. Nothing in the server
ever needs to interpret a value as text; the few places where server-side text meets client
data go through `ByteString`: `AUTH` compares bytes against the configured password's UTF-8
bytes, `CONFIG GET` and `INFO` convert their text, and `Command.ToString()` decodes
arguments as UTF-8 for logs. The client library is unchanged: it speaks UTF-8 text.

This design keeps every handler and the whole keyspace working on `string`, at the cost of
two bytes of memory per payload byte (which UTF-16 strings already cost before).

## Storage and concurrency

`Keyspace` keeps **one** `ConcurrentDictionary<string, Entry>` for all types. An entry
holds the value, an optional absolute expiry, and a version number. Because there is a
single map, a key can never hold two types at once: `SET` replaces a list, and `LPUSH` on a
string raises `WRONGTYPE`.

| Type | Value |
|---|---|
| String | `string` |
| List | `LinkedList<string>` |
| Hash | `Dictionary<string, string>` |
| Set | `HashSet<string>` |
| Sorted set | `SortedSetValue`: a dictionary for O(1) score lookup plus a red-black tree (`SortedSet<ScoredMember>`) ordered by score, then member bytes |
| Stream | `StreamLog`: entries sorted by ID, plus consumer groups |

Collections are never stored empty: removing the last field, member or element deletes the
key, as in Redis. `Keyspace` is split into partial files by type (`Keyspace.Hashes.cs`,
`Keyspace.Sets.cs`, ...).

**Locking.** Every key hashes to one of 256 lock stripes, and every operation holds its
key's stripe for its whole duration. That makes each single-key operation atomic (for
example `IncrementBy` or "pop and delete the list when it becomes empty") while operations
on different keys run in parallel. Multi-key operations (`MGET`, `MSET`, `SMOVE`, `SINTER`,
`SUNIONSTORE`, ...) take all of their stripes at once, always in ascending stripe order, so
they are atomic too and two of them can never deadlock.

**Versions.** Every change to a key (a write, a TTL change, deletion, expiry) gives it a
fresh version from a global counter. `WATCH` records a key's version and `EXEC` compares it.
While a key is watched, deleting it leaves a "tombstone" version, so delete-then-recreate
and create-then-delete are both detected; tombstones are dropped when the last watcher
leaves. In-place changes that change nothing (adding an existing set member) keep the
version.

**SCAN** orders items by a stable 64-bit FNV-1a hash of their name and uses the hash as the
cursor. A page is "every item with a hash at or above the cursor, smallest first, up to
`COUNT`" (extended so that colliding hashes never straddle two pages), and the next cursor
is the following item's hash. Because the order never changes, an item that exists for the
whole iteration is returned exactly once, whatever is added or removed in between, and
cursors survive restarts. Finding a page walks every key, so a call costs O(n) where Redis's
incremental hash-table walk is amortised O(1).

**Expiry** is stored as Unix milliseconds and read through an injectable `TimeProvider`,
so tests can move a fake clock instead of sleeping. Expired keys are invisible immediately
(every read checks), and are removed lazily on access and by a sweep once a second.

**Streams** (`StreamLog`) keep entries in a list sorted by `StreamId`, so ranges use a binary
search. `XADD *` uses `max(now, last-ms)` so IDs keep increasing even if the system clock
moves backwards. Each consumer group holds its last delivered ID, a pending entries list
(PEL: ID → owner, delivery time, delivery count) ordered by ID, and each consumer's own set
of pending IDs, so `XPENDING` ranges, `XREADGROUP` history reads and `XAUTOCLAIM` scans
walk sorted structures.

## Transactions and WATCH

Redis runs one command at a time, so `MULTI`/`EXEC` is trivially isolated. Cinnabar runs
commands from different connections in parallel, so it uses a `TransactionGate`, an async
reader/writer gate:

- Every ordinary command holds the gate in **shared** mode while it runs. Shared holders
  don't block each other, so normal throughput is unaffected.
- `EXEC` takes it **exclusively**: it waits for in-flight commands to finish, then checks
  the watched keys' versions and runs the queued commands with nothing else running. If a
  watched key changed, it returns a null array without running anything.
- An exclusive request has priority over new shared ones, so a busy server can't starve
  `EXEC`.

Commands that wait must not hold the gate while waiting, or one client blocked in
`BLPOP queue 0` would stall every `EXEC`, and with it every command queued behind that `EXEC`.
So `BLPOP`, `BRPOP`, `XREAD` and `XREADGROUP` take the gate only for each attempt to read,
and wait outside it. `PSYNC` skips the gate entirely: a full sync can take a long time, and
a snapshot taken mid-transaction is completed by the transaction's remaining writes, which
follow in the replication stream. Snapshots for `BGSAVE`, periodic and shutdown saves copy
the keys under the gate, so a file never contains half a transaction.

Lock order is always the transaction gate, then the replication `WriteBarrier`, then key
stripes, so the three can't deadlock. The gate and the barrier share one implementation,
`SharedExclusiveGate`.

## Blocking commands

`BLPOP`, `BRPOP`, `XREAD BLOCK` and `XREADGROUP BLOCK` use `KeyNotifier`. The order of
operations is what makes them correct:

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
- **Format.** RDB version 9 with a CRC-64/Jones checksum. Strings are type 0; lists, sets,
  hashes and sorted sets use the plain encodings (types 1, 2, 4 and 5, with scores as binary
  doubles), which Redis can still load. Strings are written byte for byte. Streams are
  skipped, with a warning. The reader also understands integer-encoded and LZF-compressed
  strings and the older text-score sorted sets (type 3), but not the compact encodings
  (listpacks, intsets, quicklists) that real Redis writes for small collections, so it loads
  real dumps only if they contain strings and large collections.
- **Consistency.** The keys are copied under the transaction gate before the file lock is
  taken; a copy older than the file already written is dropped instead of overwriting it.

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
