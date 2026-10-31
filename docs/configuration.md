# Configuration

The server is configured with command-line flags, optionally on top of a JSON file. Every
setting has a default, so no configuration is needed to get started.

**Precedence:** built-in defaults → JSON file (`--config`) → command-line flags.

## Command-line flags

```text
dotnet run --project src/Cinnabar.Server -- [options]
```

| Flag | Setting | Default | Description |
|---|---|---|---|
| `--config <file>` | – | – | Load settings from a JSON file. Flags still override it. |
| `--bind <address>` | `Network.BindAddress` | `0.0.0.0` | IP address to listen on. Use `127.0.0.1` to accept local connections only. |
| `--port <port>` | `Network.Port` | `6379` | TCP port. `0` picks a free port. |
| `--requirepass <password>` | `Security.RequirePass` | *(none)* | Require `AUTH` before other commands. |
| `--dir <path>` | `Persistence.Directory` | `data` | Snapshot directory, relative to the working directory. Created on first save. |
| `--dbfilename <name>` | `Persistence.DbFileName` | `dump.rdb` | Snapshot file name. |
| `--save-interval <seconds>` | `Persistence.SaveIntervalSeconds` | `0` (off) | Save a snapshot every N seconds. |
| `--replicaof <host> <port>` | `Replication.MasterHost`, `MasterPort` | *(master)* | Run as a read-only replica. `"host port"`, `host port` and `host:port` all work; `no one` keeps the server a master. |
| `--masterauth <password>` | `Replication.MasterAuth` | *(none)* | Password the replica sends to its master. |
| `--maxmemory <size>` | `Memory.MaxMemory` | `0` (no limit) | Memory limit for stored data: bytes, or a number with `kb`, `mb` or `gb` (powers of 1024; `k`, `m`, `g` are powers of 1000). See [Memory limit and eviction](#memory-limit-and-eviction). |
| `--maxmemory-policy <policy>` | `Memory.MaxMemoryPolicy` | `noeviction` | What to do over the limit: `noeviction`, `allkeys-lru`, `volatile-lru`, `allkeys-random`, `volatile-random` or `volatile-ttl`. |
| `--maxmemory-samples <n>` | `Memory.MaxMemorySamples` | `5` | Keys sampled per eviction. |
| `--metrics-port <port>` | `Diagnostics.MetricsPort` | `9090` | Prometheus endpoint port. `0` disables it. |
| `--loglevel <level>` | `Diagnostics.LogLevel` | `Information` | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` or `None`. `Trace` logs every command. |
| `--help` | – | – | Print usage and exit. |

An unknown flag or an invalid value (including an invalid memory size or policy in the JSON
file) prints an error and exits with code 2.

## JSON settings file

Property names are case-insensitive, and comments and trailing commas are allowed. Leave
out anything you don't want to change. A complete example with the defaults:

```jsonc
{
  "Network": {
    "BindAddress": "0.0.0.0",
    "Port": 6379
  },
  "Persistence": {
    "Directory": "data",
    "DbFileName": "dump.rdb",
    "LoadOnStartup": true,       // read the snapshot at startup
    "SaveOnShutdown": true,      // write it on Ctrl+C / SIGTERM
    "SaveIntervalSeconds": 0     // 0 = no periodic saves
  },
  "Replication": {
    "MasterHost": null,          // set to run as a replica
    "MasterPort": 6379,
    "MasterAuth": null
  },
  "Memory": {
    "MaxMemory": "0",            // e.g. "256mb"; "0" = no limit
    "MaxMemoryPolicy": "noeviction",
    "MaxMemorySamples": 5
  },
  "Security": {
    "RequirePass": null,
    "Username": "default",       // the only user AUTH accepts
    "MaxConnections": 10000,
    "MaxConnectionsPerIp": 1000,
    "RateLimitPerSecond": 100000, // sustained commands/second per IP; 0 disables
    "RateLimitBurst": 10000       // token bucket size per IP
  },
  "Diagnostics": {
    "LogLevel": "Information",
    "MetricsPort": 9090
  }
}
```

Some settings exist only in the file: `LoadOnStartup`, `SaveOnShutdown`, `Username`, and the
connection and rate limits.

> **Note.** The previous version (*redis-clone*) read and *rewrote* a settings file under
> `%LOCALAPPDATA%/RedisClone` on every start. Cinnabar never writes configuration; pass
> `--config` explicitly.

## Persistence behaviour

- On startup, if `<Directory>/<DbFileName>` exists, it is loaded. If the file is corrupt or
  uses a value type Cinnabar can't read, **the server refuses to start** rather than
  starting empty and later overwriting your data. Move the file away or point `--dir`
  elsewhere.
- Snapshots are written to `<file>.tmp` and then renamed over the target, so the previous
  snapshot survives a crash mid-write.
- Streams are saved with their consumer groups (pending entries, consumers and last
  delivered IDs) in a Cinnabar-specific RDB value type. A snapshot that contains a stream
  therefore loads in Cinnabar but not in Redis; snapshots without streams remain loadable by
  Redis.

## Memory limit and eviction

With `MaxMemory` above 0, every write first checks the estimated size of the stored data
(`used_memory` in `INFO memory`) against the limit. Over the limit:

- `noeviction`: the write fails with
  `OOM command not allowed when used memory > 'maxmemory'.` Commands that only remove data
  (`DEL`, `UNLINK`, `LPOP`, `LREM`, `LTRIM`, `SREM`, `HDEL`, `ZREM`, `EXPIRE`, `FLUSHDB`, ...)
  and all reads still work.
- The other policies evict keys until the data fits, then run the write. Like Redis,
  Cinnabar samples `MaxMemorySamples` random keys per eviction and removes the best
  candidate: the least recently used key (`*-lru`), the key that expires soonest
  (`volatile-ttl`), or any of them (`*-random`). `volatile-*` policies only consider keys with
  a TTL; if none can be found, the write fails with `OOM`.

Evictions are replicated to replicas as `DEL`. Replicas don't evict on their own. `INFO`
reports `used_memory`, `maxmemory`, `maxmemory_policy` and `evicted_keys`, and
`CONFIG GET maxmemory*` shows the settings.

The limit is compared with Cinnabar's *estimate* of the data's size, not the process's
memory: strings count two bytes per character plus object overhead, and a collection's size
is extrapolated from a few of its elements. The .NET runtime, connection buffers and garbage
not yet collected come on top, so leave headroom when sizing a container. The LFU policies
(`allkeys-lfu`, `volatile-lfu`) are not supported.

## Replication setup

```bash
# Master
dotnet run --project src/Cinnabar.Server -- --port 6379 --requirepass s3cret

# Replica
dotnet run --project src/Cinnabar.Server -- --port 6380 --replicaof 127.0.0.1 6379 --masterauth s3cret
```

Check the link with `redis-cli -p 6380 INFO replication` (`master_link_status:up`). Replicas
reject writes with `READONLY` and reconnect automatically if the master goes away.

## Docker

The server image listens on 6379, exposes metrics on 9090, and stores snapshots in the
`/data` volume:

```bash
docker build -f src/Cinnabar.Server/Dockerfile -t cinnabar .
docker run -p 6379:6379 -v cinnabar-data:/data cinnabar
docker run -p 6379:6379 cinnabar --requirepass s3cret   # extra flags are appended
```

`docker compose up --build` starts the server (with a named volume) and the sample web API
on port 8080. The API reaches the server through the `Redis__Host=cinnabar` environment
variable.

## Metrics

With `MetricsPort` > 0, Prometheus metrics are served at `http://<host>:<port>/metrics`:

| Metric | Type | Labels |
|---|---|---|
| `cinnabar_connections_total` | counter | |
| `cinnabar_connections_rejected_total` | counter | |
| `cinnabar_connections_active` | gauge | |
| `cinnabar_commands_total` | counter | `command`, `status` (`ok`/`error`) |
| `cinnabar_command_duration_seconds` | histogram | `command` |
| `cinnabar_rate_limited_total` | counter | |
| `cinnabar_keys` | gauge | |
| `cinnabar_memory_used_bytes` | gauge | |
| `cinnabar_evicted_keys_total` | counter | |
| `cinnabar_replication_connected_replicas` | gauge | |
| `cinnabar_replication_offset_bytes` | gauge | |

If the metrics port is already taken, the server logs a warning and keeps serving data
without metrics.
