# Command reference

Cinnabar implements 53 commands. Names are case-insensitive. Behaviour follows Redis unless
a **Note** says otherwise. Run `COMMAND` against a server to list what it supports.

Legend: ✍️ write command (rejected on replicas, propagated to replicas) · ⏳ may block.

- [Strings](#strings)
- [Keys](#keys)
- [Lists](#lists)
- [Streams](#streams)
- [Pub/Sub](#pubsub)
- [Transactions](#transactions)
- [Connection](#connection)
- [Server](#server)
- [Replication](#replication)
- [Error replies](#error-replies)

## Strings

| Command | Syntax | Reply |
|---|---|---|
| `GET` | `GET key` | Value, or nil. `WRONGTYPE` if the key holds a list or stream. |
| `SET` ✍️ | `SET key value [NX \| XX] [EX s \| PX ms \| EXAT unix-s \| PXAT unix-ms \| KEEPTTL]` | `OK`, or nil when `NX`/`XX` prevented the write. Replaces a value of any type. |
| `INCR` ✍️ | `INCR key` | New value. A missing key starts at 0. Keeps the key's TTL. |
| `DECR` ✍️ | `DECR key` | New value. |
| `INCRBY` ✍️ | `INCRBY key increment` | New value. Overflow → `ERR increment or decrement would overflow`. |
| `DECRBY` ✍️ | `DECRBY key decrement` | New value. |

**Notes.** `SET ... GET` is not supported. An `EXAT`/`PXAT` time in the past deletes the key.

## Keys

| Command | Syntax | Reply |
|---|---|---|
| `DEL` ✍️ | `DEL key [key ...]` | Number of keys removed. |
| `EXISTS` | `EXISTS key [key ...]` | How many of the keys exist; a key listed twice counts twice. |
| `TYPE` | `TYPE key` | `string`, `list`, `stream` or `none`. |
| `KEYS` | `KEYS pattern` | Matching keys, sorted. Glob syntax: `*`, `?`, `[abc]`, `[^a]`, `[a-z]`, `\x` escapes. |
| `EXPIRE` ✍️ | `EXPIRE key seconds` | `1` if set, `0` if the key does not exist. A TTL ≤ 0 deletes the key. |
| `PEXPIRE` ✍️ | `PEXPIRE key milliseconds` | As `EXPIRE`. |
| `EXPIREAT` ✍️ | `EXPIREAT key unix-seconds` | As `EXPIRE`. |
| `PEXPIREAT` ✍️ | `PEXPIREAT key unix-milliseconds` | As `EXPIRE`. |
| `PERSIST` ✍️ | `PERSIST key` | `1` if a TTL was removed, otherwise `0`. |
| `TTL` | `TTL key` | Seconds left (rounded), `-1` without a TTL, `-2` if missing. |
| `PTTL` | `PTTL key` | Milliseconds left, `-1`, or `-2`. |

Expiry works on every type, not just strings.

## Lists

| Command | Syntax | Reply |
|---|---|---|
| `LPUSH` ✍️ | `LPUSH key element [element ...]` | New length. `LPUSH k a b c` gives `[c, b, a]`. |
| `RPUSH` ✍️ | `RPUSH key element [element ...]` | New length. |
| `LPOP` ✍️ | `LPOP key [count]` | Without `count`: the element or nil. With `count`: an array, or a null array if the key does not exist. |
| `RPOP` ✍️ | `RPOP key [count]` | As `LPOP`, from the tail. |
| `LLEN` | `LLEN key` | Length; `0` if missing. |
| `LRANGE` | `LRANGE key start stop` | Elements from `start` to `stop` inclusive. Negative indexes count from the end; out-of-range indexes are clamped. |
| `BLPOP` ✍️⏳ | `BLPOP key [key ...] timeout` | `[key, element]` from the first non-empty list, or a null array after `timeout` seconds (decimals allowed; `0` waits forever). |
| `BRPOP` ✍️⏳ | `BRPOP key [key ...] timeout` | As `BLPOP`, from the tail. |

A list that becomes empty is deleted. A blocked client that disconnects stops waiting
within about 100 ms. Inside `EXEC`, `BLPOP`/`BRPOP` never block.

## Streams

| Command | Syntax | Reply |
|---|---|---|
| `XADD` ✍️ | `XADD key id field value [field value ...]` | The entry ID. `id` is `*` (auto), `ms-*` (auto sequence), `ms-seq`, or `ms` (sequence 0). Must be greater than the top entry and than `0-0`. |
| `XLEN` | `XLEN key` | Number of entries. |
| `XRANGE` | `XRANGE key start end [COUNT count]` | `[[id, [field, value, ...]], ...]`. `-` and `+` are the minimum and maximum; prefix an ID with `(` to exclude it; a bare `ms` means `ms-0` as a start and `ms-<max>` as an end. |
| `XREAD` ⏳ | `XREAD [COUNT count] [BLOCK ms] STREAMS key [key ...] id [id ...]` | `[[key, entries], ...]` for streams with entries newer than the given IDs, or nil. `$` means "newer than the current last entry". `BLOCK 0` waits forever. |

**Notes.** `XREVRANGE`, `XDEL`, `XTRIM`, and consumer groups (`XGROUP`, `XREADGROUP`, `XACK`) are
not implemented. Streams are not written to RDB snapshots.

## Pub/Sub

| Command | Syntax | Reply |
|---|---|---|
| `SUBSCRIBE` | `SUBSCRIBE channel [channel ...]` | One `["subscribe", channel, count]` push per channel. Messages then arrive as `["message", channel, payload]`. |
| `UNSUBSCRIBE` | `UNSUBSCRIBE [channel ...]` | One `["unsubscribe", channel, remaining]` push per channel; with no channels, leaves all of them. |
| `PUBLISH` | `PUBLISH channel message` | Number of subscribers that received the message. |

While subscribed, a connection may only send `SUBSCRIBE`, `UNSUBSCRIBE`, `PING` (which then
replies `["pong", message]`) and `QUIT`. Pattern subscriptions (`PSUBSCRIBE`) are not
implemented, and `PUBLISH` is not replicated.

## Transactions

| Command | Syntax | Reply |
|---|---|---|
| `MULTI` | `MULTI` | `OK`. Later commands reply `QUEUED` until `EXEC` or `DISCARD`. |
| `EXEC` | `EXEC` | An array with one reply per queued command. `EXECABORT` if any command was rejected while queueing (unknown name, wrong argument count). |
| `DISCARD` | `DISCARD` | `OK`; the queue is dropped. |

**Note.** Queued commands run back to back, but commands from other clients may run in
between: transactions are not isolated, and `WATCH` is not implemented.

## Connection

| Command | Syntax | Reply |
|---|---|---|
| `PING` | `PING [message]` | `PONG`, or the message as a bulk string. |
| `ECHO` | `ECHO message` | The message. |
| `AUTH` | `AUTH [username] password` | `OK`, or `WRONGPASS`. The username must match `Security.Username` (default `default`). Errors when no password is configured. |
| `QUIT` | `QUIT` | `OK`, then the server closes the connection. |
| `SELECT` | `SELECT index` | `OK` for `0`; there is only one database. |
| `CLIENT` | `CLIENT ID \| GETNAME \| SETNAME name \| SETINFO attr value` | The subset client libraries send on connect. |

## Server

| Command | Syntax | Reply |
|---|---|---|
| `CONFIG` | `CONFIG GET pattern [pattern ...]` | Matching name/value pairs: `bind`, `port`, `dir`, `dbfilename`, `requirepass`, `masterauth`, `replicaof`, `maxclients`, `save`, `appendonly`, `databases`. `CONFIG SET` is not supported. |
| `INFO` | `INFO [section]` | Text with `Server`, `Clients`, `Persistence`, `Replication` and `Keyspace` sections. |
| `COMMAND` | `COMMAND [COUNT \| DOCS \| INFO ...]` | Command names; `COUNT` returns how many. |
| `DBSIZE` | `DBSIZE` | Number of keys. |
| `FLUSHDB` ✍️ | `FLUSHDB [ASYNC \| SYNC]` | `OK`; deletes every key. |
| `FLUSHALL` ✍️ | `FLUSHALL [ASYNC \| SYNC]` | Same as `FLUSHDB`. |
| `SAVE` | `SAVE` | `OK` once the snapshot is written. |
| `BGSAVE` | `BGSAVE` | `Background saving started`, or an error if a save is already running. |
| `LASTSAVE` | `LASTSAVE` | Unix time of the last successful save; `0` if none. |

## Replication

| Command | Syntax | Reply |
|---|---|---|
| `REPLCONF` | `REPLCONF listening-port port \| capa capability \| ACK offset` | `OK`; `ACK` gets no reply. Sent by replicas. |
| `PSYNC` | `PSYNC replicationid offset` | Always a full resync: `+FULLRESYNC <id> <offset>`, then the RDB snapshot, then the command stream. |
| `WAIT` ⏳ | `WAIT numreplicas timeout` | How many replicas have applied this client's writes, after waiting up to `timeout` ms (`0` = forever). |

## Error replies

| Prefix | When |
|---|---|
| `ERR` | Generic errors: unknown command, wrong number of arguments, syntax errors, values that aren't integers, rate limiting. |
| `WRONGTYPE` | The key holds a different type than the command works on. |
| `NOAUTH` | `requirepass` is set and the connection hasn't authenticated. |
| `WRONGPASS` | `AUTH` failed. |
| `READONLY` | A write was sent to a replica. |
| `EXECABORT` | `EXEC` after a command was rejected while queueing. |
