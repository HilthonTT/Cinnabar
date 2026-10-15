# Changelog

## 1.0.0: redis-clone becomes Cinnabar

The project is renamed **Cinnabar** and restructured into class libraries. Most of the code
was rewritten on the way, and a long list of bugs was fixed.

### Renamed

| Before | After |
|---|---|
| `redis-clone.slnx` | `Cinnabar.slnx` |
| `src/RedisClone.CLI` (server) | `src/Cinnabar.Server` |
| `src/RedisClone.Client` | `src/Cinnabar.Client` |
| `src/RedisClone.CLI.Tests` | `tests/Cinnabar.Server.Tests` (plus four new test projects) |
| `src/WebApp.Example` | `samples/Cinnabar.Samples.WebApi` |
| Namespaces `RedisClone.*` | `Cinnabar.*` |
| Metrics prefix `redis_clone_server_` | `cinnabar_` |

### New structure

- **`Cinnabar.Protocol`**: one RESP2 reader/encoder shared by server and client. Previously
  each had its own copy, and the server also had a third, string-splitting parser for replication.
- **`Cinnabar.Storage`**: the keyspace and RDB persistence as a library with no networking dependencies.
- **Server feature folders**: `Commands/<Family>`, `Networking`, `Replication`, `Persistence`,
  `PubSub`, `Blocking`, `Security`, `Configuration`, `Diagnostics`, `Hosting`.
- **One handler shape**: `ValueTask<Reply> ExecuteAsync(...)` with a declarative
  `[Command("NAME", MinArgs, MaxArgs, Flags)]` attribute. This replaces the
  `Handle`/`HandleAsync`/`LongOperation` split, the `CommandType` enum and the three
  validation attributes. Handlers are discovered automatically.
- `Microsoft.Extensions.Logging` replaces `Console.WriteLine` (the server used to print
  several lines for every command).
- Shared build settings live in `Directory.Build.props`.
- Docs: rewritten README plus `docs/` (architecture, commands, configuration, client guide).

### Fixed: server

- **Every command failed with `NOAUTH`** unless the caller remembered to set the auth state
  on the connection. This broke 15 of the existing tests.
- **Replication didn't work.** Writes were never propagated (`PropagateCommand` had no
  callers), and there were no `PSYNC`, `REPLCONF` or `INFO` handlers, so a replica handshake
  could not complete. The replica also split its incoming byte stream on every `*` byte,
  which corrupted any value containing `*` and any command split across TCP reads.
  Replication now works end to end: snapshot transfer, ordered streaming, `WAIT`, reconnects.
- Replicas started from a hard-coded empty RDB, so data written before they connected was
  missing. They now receive a real snapshot, taken consistently with the replication
  offset (see `WriteBarrier`).
- `XADD *` and relative expiries were replayed on replicas, producing different IDs and
  expiry times. They are now replicated as resolved IDs and absolute times.
- `DEL`, `EXPIRE`, `PEXPIRE`, `LPOP`, `RPOP` and `BLPOP` were never replicated.
- Replicas accepted writes from clients. They now return `READONLY`.
- **`XREAD BLOCK` never woke up**: `XADD` published a different event type from the one
  `XREAD` subscribed to, and `$` was never resolved to the last ID.
- **`BLPOP` could miss pushes or hang**: a push between the first check and subscribing was
  lost; with several waiters, every notification went to the first one; an invalid timeout
  threw and killed the connection; the timeout used the current culture (`1,5` vs `1.5`).
- A client that disconnected while blocked in `BLPOP` stayed registered and could swallow
  a later push.
- **Connections were never cleaned up**: `ClientConnection` was never disposed, and pub/sub
  subscriptions were never removed on disconnect, leaking memory and queuing messages forever.
- The rate limiter's refill rate rounded to zero, so after its initial burst a client was
  throttled **forever**.
- **Invalid RESP**: empty arrays were sent as `$*0` (for `XRANGE`, `LRANGE`, `LPOP`/`RPOP` on a
  missing key), and bulk-string arrays used character counts instead of UTF-8 byte counts,
  which corrupted the stream for non-ASCII values.
- A key could hold a string and a list at the same time (`SET` didn't replace lists, and
  `LPUSH` didn't check types). There is now one keyspace and `WRONGTYPE` errors.
- `INCR`/`INCRBY` removed the key's TTL and didn't detect overflow.
- `EXISTS` and `UNSUBSCRIBE` ignored every argument after the first.
- `KEYS` without an argument crashed the connection; only `*` was supported, and expired
  keys were listed.
- `ECHO` replied with a simple string (broken by newlines) instead of a bulk string.
- `SUBSCRIBE` wrapped its confirmations in an extra array.
- Subscribed mode allowed every command not explicitly marked otherwise, instead of only
  `(UN)SUBSCRIBE`/`PING`/`QUIT`.
- Pub/sub messages and replies could interleave mid-frame on the same socket.
- `CONFIG GET` of an unknown parameter returned a null bulk string instead of an empty array.
- `TTL` truncated instead of rounding, and returned `-2` for lists and streams.
- `EXEC` didn't abort after a rejected command (`EXECABORT`).
- `WAIT` could hang past its timeout when a replica was dead.
- A malformed request (or an unexpected exception in any handler) terminated the connection
  with no reply.
- Untrusted length headers were unbounded: `*2147483647` made the server allocate a huge array.
- The settings file was written with `File.OpenWrite`, which doesn't truncate, leaving
  corrupt JSON when the new content was shorter. A stale `--replicaof` saved in that file
  also made a later master try to replicate. Settings are no longer written at all.
- The process-wide `AppSettings.Default` instance was mutated at startup and shared between tests.
- **RDB checksums were incompatible with Redis.** The CRC-64 table was built with the
  reflected (right-shift) algorithm but the unreflected Jones polynomial, so real Redis
  dumps failed verification. Verification also read the stored checksum with host-endian
  `BitConverter`. Checksums now match Redis (tested against the published check value).
- The RDB reader didn't support LZF-compressed strings (common in real Redis dumps),
  misread signed integer encodings, and failed on `0xF8`/`0xF9` opcodes. A corrupt
  snapshot was silently ignored, and the empty dataset was then saved on shutdown.
- The metrics port being in use crashed startup.

### Fixed: client

- A cancelled or failed command returned its connection to the pool with the reply still
  unread, so **the next caller received the previous command's reply**. Interrupted
  connections are now discarded.
- `MultiAsync`/`ExecAsync`/`DiscardAsync` sent each command on a different pooled
  connection, so transactions never worked. They are replaced by `ExecuteTransactionAsync`.
- `BLPopAsync` formatted the timeout with the current culture (`"1,5"` in French/Belgian locales).
- `LPopAsync(key, count)` threw on a missing key.
- `SubscribeAsync` returned before the server had confirmed the subscription, so early
  messages were lost.
- Returning a connection after the pool was disposed threw `ObjectDisposedException`.
- The sample API built Server-Sent Events JSON by string interpolation, which broke on quotes.
- The sample API pulled in `Microsoft.OpenApi` 2.4.1, affected by GHSA-v5pm-xwqc-g5wc; it is
  now pinned to 2.7.6.

### Added

- Commands: `SET` options (`NX`, `XX`, `EX`, `PX`, `EXAT`, `PXAT`, `KEEPTTL`), `EXPIREAT`,
  `PEXPIREAT`, `PERSIST`, `PTTL`, `BRPOP`, multi-key `BLPOP`, `XLEN`, exclusive `XRANGE`
  bounds, `DBSIZE`, `FLUSHDB`, `FLUSHALL`, `SAVE`, `BGSAVE`, `LASTSAVE`, `INFO`, `COMMAND`,
  `QUIT`, `SELECT`, `CLIENT`, `PSYNC`, `REPLCONF`, and `KEYS` glob patterns.
- Inline commands (`PING\r\n` from telnet/netcat).
- Expiry on every key type, with an injectable clock.
- RDB snapshot writing: `SAVE`, `BGSAVE`, periodic and on-shutdown saves, atomic file replacement.
- Graceful shutdown on Ctrl+C and SIGTERM.
- Flags `--bind`, `--requirepass`, `--masterauth`, `--save-interval`, `--metrics-port`,
  `--loglevel`, `--config`, `--help`; `--replicaof host port` as two tokens.
- `AUTH user pass` now checks the username.
- Client: `DeleteAsync`, `ExistsAsync`, `ExpireAsync`, `PersistAsync`, `TimeToLiveAsync`,
  `SetIfNotExistsAsync`, `RPopAsync`, multi-key `BLPopAsync`, `XLenAsync`, `XRangeAsync`,
  `InfoAsync`, `DbSizeAsync`, `FlushDbAsync`, `ExecuteTransactionAsync`.
- Test projects for Protocol, Storage, Client and end-to-end integration (including replication).

### Breaking changes

- All namespaces, project names and assembly names changed (see *Renamed*).
- Client: `RespValue` now lives in `Cinnabar.Protocol`. `RedisException` and
  `RedisConnectionException` moved to `Cinnabar.Client`, and `ProtocolViolationException` is
  gone (protocol errors surface as `RedisConnectionException`). `MultiAsync`, `ExecAsync`,
  `DiscardAsync`, `AuthAsync` and `TTLAsync` were removed: use `ExecuteTransactionAsync`,
  `Options.Password` and `TimeToLiveAsync`. `RedisConnection` and `ConnectionPool` are now
  internal. Reading `RedisClientOptions.ConnectionString` masks the password.
- Server: the settings file under `%LOCALAPPDATA%/RedisClone` is no longer read or written;
  use `--config`. The default snapshot is now `./data/dump.rdb` (was
  `%LOCALAPPDATA%/RedisClone/backup.rdb`). The default per-IP limits are higher (1,000
  connections, 100,000 commands/s) because the old defaults throttled ordinary load tests.
