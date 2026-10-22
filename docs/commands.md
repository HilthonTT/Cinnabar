# Command reference

Cinnabar implements 120 commands. Names are case-insensitive. Behaviour follows Redis unless
a **Note** says otherwise. Run `COMMAND` against a server to list what it supports.

Legend: ✍️ write command (rejected on replicas, propagated to replicas) · ⏳ may block.

Keys, values, fields and members are binary-safe: any bytes, including invalid UTF-8, are
stored and returned exactly, and lengths (`STRLEN`, `HSTRLEN`) count bytes.

- [Strings](#strings)
- [Keys](#keys)
- [Lists](#lists)
- [Hashes](#hashes)
- [Sets](#sets)
- [Sorted sets](#sorted-sets)
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
| `GET` | `GET key` | Value, or nil. `WRONGTYPE` if the key holds another type. |
| `SET` ✍️ | `SET key value [NX \| XX] [EX s \| PX ms \| EXAT unix-s \| PXAT unix-ms \| KEEPTTL]` | `OK`, or nil when `NX`/`XX` prevented the write. Replaces a value of any type. |
| `MGET` | `MGET key [key ...]` | The values, read atomically; nil for missing keys and keys of other types. |
| `MSET` ✍️ | `MSET key value [key value ...]` | `OK`. Sets every key atomically, replacing values of any type and clearing TTLs. |
| `MSETNX` ✍️ | `MSETNX key value [key value ...]` | `1` if the keys were set, `0` (and nothing written) if any of them exists. |
| `STRLEN` | `STRLEN key` | Length in bytes; `0` if missing. |
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
| `TYPE` | `TYPE key` | `string`, `list`, `stream`, `hash`, `set`, `zset` or `none`. |
| `KEYS` | `KEYS pattern` | Matching keys, sorted. Glob syntax: `*`, `?`, `[abc]`, `[^a]`, `[a-z]`, `\x` escapes. |
| `SCAN` | `SCAN cursor [MATCH pattern] [COUNT count] [TYPE type]` | `[next-cursor, [key, ...]]`. Start with `0`; the iteration is done when the cursor comes back as `0`. |
| `EXPIRE` ✍️ | `EXPIRE key seconds` | `1` if set, `0` if the key does not exist. A TTL ≤ 0 deletes the key. |
| `PEXPIRE` ✍️ | `PEXPIRE key milliseconds` | As `EXPIRE`. |
| `EXPIREAT` ✍️ | `EXPIREAT key unix-seconds` | As `EXPIRE`. |
| `PEXPIREAT` ✍️ | `PEXPIREAT key unix-milliseconds` | As `EXPIRE`. |
| `PERSIST` ✍️ | `PERSIST key` | `1` if a TTL was removed, otherwise `0`. |
| `TTL` | `TTL key` | Seconds left (rounded), `-1` without a TTL, `-2` if missing. |
| `PTTL` | `PTTL key` | Milliseconds left, `-1`, or `-2`. |

Expiry works on every type, not just strings.

**SCAN.** A key that exists for the whole iteration is returned exactly once, however many
keys are added or removed in between; a key added or removed meanwhile may or may not
appear. `COUNT` (default 10) is how many keys are examined per call; `MATCH` and `TYPE`
filter those, so a page can be empty before the end, as in Redis. The cursor is a position
in a fixed hash order, so it stays valid across restarts and on replicas. Unlike Redis, each
call walks the whole keyspace to find its page, so `SCAN` avoids `KEYS`'s huge reply but not
its O(n) work. `HSCAN`, `SSCAN` and `ZSCAN` work the same way on a single key.

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

## Hashes

| Command | Syntax | Reply |
|---|---|---|
| `HSET` ✍️ | `HSET key field value [field value ...]` | Number of fields that were added (not updated). |
| `HMSET` ✍️ | `HMSET key field value [field value ...]` | `OK`. The deprecated form of `HSET`. |
| `HSETNX` ✍️ | `HSETNX key field value` | `1` if the field was set, `0` if it already existed. |
| `HGET` | `HGET key field` | The value, or nil. |
| `HMGET` | `HMGET key field [field ...]` | The values; nil for missing fields. |
| `HGETALL` | `HGETALL key` | `[field, value, ...]` in no particular order. |
| `HKEYS` | `HKEYS key` | Every field. |
| `HVALS` | `HVALS key` | Every value. |
| `HDEL` ✍️ | `HDEL key field [field ...]` | Number of fields removed. |
| `HEXISTS` | `HEXISTS key field` | `1` or `0`. |
| `HLEN` | `HLEN key` | Number of fields; `0` if missing. |
| `HSTRLEN` | `HSTRLEN key field` | Length of the value in bytes; `0` if missing. |
| `HINCRBY` ✍️ | `HINCRBY key field increment` | New value. A missing field starts at 0. |
| `HINCRBYFLOAT` ✍️ | `HINCRBYFLOAT key field increment` | New value as a bulk string, such as `10.6`. |
| `HSCAN` | `HSCAN key cursor [MATCH pattern] [COUNT count]` | `[next-cursor, [field, value, ...]]`. See [SCAN](#keys). |

A hash that loses its last field is deleted. `HINCRBYFLOAT` is replicated as `HSET` with the
result, so replicas store exactly the master's value.

## Sets

| Command | Syntax | Reply |
|---|---|---|
| `SADD` ✍️ | `SADD key member [member ...]` | Number of members that were added. |
| `SREM` ✍️ | `SREM key member [member ...]` | Number of members removed. |
| `SISMEMBER` | `SISMEMBER key member` | `1` or `0`. |
| `SMISMEMBER` | `SMISMEMBER key member [member ...]` | `1` or `0` for each member. |
| `SMEMBERS` | `SMEMBERS key` | Every member, in no particular order. |
| `SCARD` | `SCARD key` | Number of members; `0` if missing. |
| `SPOP` ✍️ | `SPOP key [count]` | Without `count`: a random member, or nil. With `count`: up to that many distinct members. |
| `SRANDMEMBER` | `SRANDMEMBER key [count]` | Like `SPOP` without removing. A negative count returns exactly `\|count\|` members, which may repeat. |
| `SMOVE` ✍️ | `SMOVE source destination member` | `1` if the member was moved, `0` if it was not in `source`. Atomic. |
| `SINTER` | `SINTER key [key ...]` | Members in every set. A missing key is an empty set. |
| `SUNION` | `SUNION key [key ...]` | Members in any set. |
| `SDIFF` | `SDIFF key [key ...]` | Members of the first set that are in none of the others. |
| `SINTERSTORE` ✍️ | `SINTERSTORE destination key [key ...]` | Size of the result, which replaces `destination` (an empty result deletes it). |
| `SUNIONSTORE` ✍️ | `SUNIONSTORE destination key [key ...]` | As `SINTERSTORE`, with the union. |
| `SDIFFSTORE` ✍️ | `SDIFFSTORE destination key [key ...]` | As `SINTERSTORE`, with the difference. |
| `SINTERCARD` | `SINTERCARD numkeys key [key ...] [LIMIT limit]` | Size of the intersection, stopping at `limit` (`0` = no limit). |
| `SSCAN` | `SSCAN key cursor [MATCH pattern] [COUNT count]` | `[next-cursor, [member, ...]]`. See [SCAN](#keys). |

A set that loses its last member is deleted. Multi-key commands (`SINTER`, `SMOVE`,
`S*STORE`, ...) see and change all their keys atomically. `SPOP` is replicated as `SREM` of
the members it removed, so replicas don't pick their own random members.

## Sorted sets

| Command | Syntax | Reply |
|---|---|---|
| `ZADD` ✍️ | `ZADD key [NX \| XX] [GT \| LT] [CH] [INCR] score member [score member ...]` | Number of members added (with `CH`: added or changed). With `INCR`: the new score, or nil if a flag prevented the update. |
| `ZINCRBY` ✍️ | `ZINCRBY key increment member` | The new score. |
| `ZREM` ✍️ | `ZREM key member [member ...]` | Number of members removed. |
| `ZSCORE` | `ZSCORE key member` | The score, or nil. |
| `ZMSCORE` | `ZMSCORE key member [member ...]` | The scores; nil for missing members. |
| `ZCARD` | `ZCARD key` | Number of members; `0` if missing. |
| `ZCOUNT` | `ZCOUNT key min max` | Members with a score in the range. |
| `ZLEXCOUNT` | `ZLEXCOUNT key min max` | Members in a lexicographic range. |
| `ZRANK` | `ZRANK key member [WITHSCORE]` | 0-based rank by ascending score (with `WITHSCORE`: `[rank, score]`), or nil. |
| `ZREVRANK` | `ZREVRANK key member [WITHSCORE]` | As `ZRANK`, by descending score. |
| `ZRANGE` | `ZRANGE key start stop [BYSCORE \| BYLEX] [REV] [LIMIT offset count] [WITHSCORES]` | Members by rank (default), score or lex range; `[member, score, ...]` with `WITHSCORES`. |
| `ZREVRANGE` | `ZREVRANGE key start stop [WITHSCORES]` | Members by rank, highest score first. |
| `ZRANGEBYSCORE` | `ZRANGEBYSCORE key min max [WITHSCORES] [LIMIT offset count]` | Members with a score in the range. |
| `ZREVRANGEBYSCORE` | `ZREVRANGEBYSCORE key max min [WITHSCORES] [LIMIT offset count]` | As above, highest first. |
| `ZRANGEBYLEX` | `ZRANGEBYLEX key min max [LIMIT offset count]` | Members in a lexicographic range. |
| `ZREVRANGEBYLEX` | `ZREVRANGEBYLEX key max min [LIMIT offset count]` | As above, in reverse. |
| `ZPOPMIN` ✍️ | `ZPOPMIN key [count]` | Removes and returns the lowest-scored members as `[member, score, ...]`. |
| `ZPOPMAX` ✍️ | `ZPOPMAX key [count]` | As `ZPOPMIN`, highest first. |
| `ZREMRANGEBYRANK` ✍️ | `ZREMRANGEBYRANK key start stop` | Number of members removed. |
| `ZREMRANGEBYSCORE` ✍️ | `ZREMRANGEBYSCORE key min max` | Number of members removed. |
| `ZREMRANGEBYLEX` ✍️ | `ZREMRANGEBYLEX key min max` | Number of members removed. |
| `ZSCAN` | `ZSCAN key cursor [MATCH pattern] [COUNT count]` | `[next-cursor, [member, score, ...]]`. See [SCAN](#keys). |

**Ranges.** Ranks use `LRANGE` rules (negative counts from the end). Score bounds are
numbers, `-inf` or `+inf`; prefix one with `(` to make it exclusive. Lex bounds are
`[member` (inclusive), `(member` (exclusive), `-` and `+`; they are meant for sets whose
members all have the same score. With `ZRANGE ... BYSCORE REV` or `BYLEX REV`, give the
upper bound first.

**Notes.** Members with equal scores are ordered by their bytes. Scores are formatted with
the shortest text that round-trips (`1`, `1.5`, `1e+20`, `inf`, `-inf`). Rank lookups and
rank ranges cost O(rank) rather than Redis's O(log n). `ZUNIONSTORE`, `ZINTERSTORE`,
`ZRANGESTORE`, `ZRANDMEMBER` and the blocking `BZPOPMIN`/`BZPOPMAX` are not implemented.

## Streams

| Command | Syntax | Reply |
|---|---|---|
| `XADD` ✍️ | `XADD key id field value [field value ...]` | The entry ID. `id` is `*` (auto), `ms-*` (auto sequence), `ms-seq`, or `ms` (sequence 0). Must be greater than the top entry and than `0-0`. |
| `XLEN` | `XLEN key` | Number of entries. |
| `XRANGE` | `XRANGE key start end [COUNT count]` | `[[id, [field, value, ...]], ...]`. `-` and `+` are the minimum and maximum; prefix an ID with `(` to exclude it; a bare `ms` means `ms-0` as a start and `ms-<max>` as an end. |
| `XREAD` ⏳ | `XREAD [COUNT count] [BLOCK ms] STREAMS key [key ...] id [id ...]` | `[[key, entries], ...]` for streams with entries newer than the given IDs, or nil. `$` means "newer than the current last entry". `BLOCK 0` waits forever. |

### Consumer groups

| Command | Syntax | Reply |
|---|---|---|
| `XGROUP` ✍️ | `XGROUP CREATE key group id\|$ [MKSTREAM]` | `OK`; `BUSYGROUP` if the group exists. Without `MKSTREAM` the stream must exist. |
| | `XGROUP SETID key group id\|$` | `OK`; moves the group's last delivered ID. |
| | `XGROUP DESTROY key group` | `1` if the group existed. |
| | `XGROUP CREATECONSUMER key group consumer` | `1` if the consumer was created. |
| | `XGROUP DELCONSUMER key group consumer` | Number of pending entries the consumer had (they are dropped). |
| `XREADGROUP` ✍️⏳ | `XREADGROUP GROUP group consumer [COUNT count] [BLOCK ms] [NOACK] STREAMS key [key ...] id [id ...]` | Like `XREAD`. `>` delivers entries no consumer has received and adds them to this consumer's pending list (unless `NOACK`); any other ID re-reads this consumer's own pending entries after it. |
| `XACK` ✍️ | `XACK key group id [id ...]` | Number of entries that were pending and are now acknowledged. |
| `XPENDING` | `XPENDING key group` | `[count, smallest-id, largest-id, [[consumer, count], ...]]`. |
| | `XPENDING key group [IDLE ms] start end count [consumer]` | `[[id, consumer, idle-ms, delivery-count], ...]`. |
| `XCLAIM` ✍️ | `XCLAIM key group consumer min-idle-time id [id ...] [IDLE ms] [TIME unix-ms] [RETRYCOUNT n] [FORCE] [JUSTID] [LASTID id]` | The entries (or, with `JUSTID`, the IDs) that were pending for at least `min-idle-time` ms and now belong to `consumer`. |
| `XAUTOCLAIM` ✍️ | `XAUTOCLAIM key group consumer min-idle-time start [COUNT count] [JUSTID]` | `[next-start, entries, deleted-ids]`. Scans the pending list from `start`; `next-start` is `0-0` when done. |

**Notes.** `XREADGROUP` only blocks when every ID is `>`; inside `EXEC` it never blocks.
`XREADGROUP`, `XCLAIM` and `XAUTOCLAIM` are replicated as `XCLAIM ... TIME ... FORCE LASTID`
commands that name the exact entries delivered or claimed (and `XGROUP SETID` for `NOACK`),
so replicas end up with the same pending entries without depending on their own clock.
`ENTRIESREAD` is accepted and ignored. `XINFO`, `XREVRANGE`, `XDEL` and `XTRIM` are not
implemented. Streams, and so their consumer groups, are not written to RDB snapshots.

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
| `EXEC` | `EXEC` | An array with one reply per queued command. A null array if a watched key changed. `EXECABORT` if any command was rejected while queueing (unknown name, wrong argument count). |
| `DISCARD` | `DISCARD` | `OK`; the queue is dropped. |
| `WATCH` | `WATCH key [key ...]` | `OK`. The next `EXEC` fails (null array) if any of the keys is written, deleted, expires or is flushed before it runs, by any client including this one. `ERR` inside `MULTI`. |
| `UNWATCH` | `UNWATCH` | `OK`; forgets every watched key. |

`EXEC` is isolated: once it starts, no other client's command runs until it finishes, and
the `WATCH` check happens inside that window. Watches end at `EXEC` (whatever the outcome),
`DISCARD`, `UNWATCH`, and when the connection closes. A write that changes nothing (`SADD`
of an existing member, `HDEL` of a missing field) does not count as a change. Each queued
write is replicated as its own command, so a replica can briefly show part of a transaction.

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
| `ERR` | Generic errors: unknown command, wrong number of arguments, syntax errors, values that aren't integers or floats, rate limiting. |
| `WRONGTYPE` | The key holds a different type than the command works on. |
| `NOAUTH` | `requirepass` is set and the connection hasn't authenticated. |
| `WRONGPASS` | `AUTH` failed. |
| `READONLY` | A write was sent to a replica. |
| `EXECABORT` | `EXEC` after a command was rejected while queueing. |
| `BUSYGROUP` | `XGROUP CREATE` for a group that already exists. |
| `NOGROUP` | A consumer-group command named a stream or group that doesn't exist. |
