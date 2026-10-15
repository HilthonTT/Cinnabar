using Cinnabar.Server.Configuration;
using Cinnabar.Storage;
using Cinnabar.Storage.Persistence;
using Microsoft.Extensions.Logging;

namespace Cinnabar.Server.Persistence;

/// <summary>
/// Loads the RDB snapshot at startup and writes it on SAVE/BGSAVE, on a timer, and at shutdown.
/// </summary>
internal sealed class SnapshotService(
    ServerSettings settings,
    Keyspace keyspace,
    TimeProvider timeProvider,
    ILogger<SnapshotService> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private int _disposeState;
    private Task? _periodicSaves;
    private Task? _backgroundSave;

    /// <summary>When the last successful save finished, if any.</summary>
    public DateTimeOffset? LastSave { get; private set; }

    /// <summary><see langword="true"/> while BGSAVE is running.</summary>
    public bool IsBackgroundSaveRunning => _backgroundSave is { IsCompleted: false };

    /// <summary>
    /// Loads the snapshot file, if present.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file exists but cannot be read. Startup is aborted on purpose: continuing with an
    /// empty dataset and saving at shutdown would overwrite the user's data.
    /// </exception>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        string path = settings.Persistence.FilePath;
        if (!settings.Persistence.LoadOnStartup || !File.Exists(path))
        {
            return;
        }

        RdbSnapshot snapshot;
        try
        {
            snapshot = await RdbReader.ReadFileAsync(path, cancellationToken);
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException)
        {
            throw new InvalidDataException(
                $"Could not load snapshot '{path}': {e.Message} Move the file away or start with a different --dir/--dbfilename.",
                e);
        }

        int loaded = keyspace.Load(snapshot.Databases.GetValueOrDefault(0) ?? []);
        int ignoredDatabases = snapshot.Databases.Keys.Count(db => db != 0);
        logger.LogInformation("Loaded {Count} key(s) from {Path}.", loaded, path);

        if (ignoredDatabases > 0)
        {
            logger.LogWarning("Ignored {Count} non-zero database(s) in the snapshot; Cinnabar has a single database.", ignoredDatabases);
        }
    }

    /// <summary>Writes the snapshot now (SAVE).</summary>
    public async Task<RdbWriteResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            string path = settings.Persistence.FilePath;
            RdbWriteResult result = await RdbWriter.WriteFileAsync(path, keyspace.Snapshot(), cancellationToken);
            LastSave = timeProvider.GetUtcNow();

            logger.LogInformation("Saved {Count} key(s) to {Path}.", result.WrittenKeys, path);
            if (result.SkippedKeys > 0)
            {
                logger.LogWarning("{Count} stream key(s) were not saved; streams are not persisted.", result.SkippedKeys);
            }

            return result;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>Starts a save in the background (BGSAVE).</summary>
    /// <returns><see langword="false"/> if one is already running.</returns>
    public bool TryStartBackgroundSave()
    {
        if (IsBackgroundSaveRunning)
        {
            return false;
        }

        _backgroundSave = Task.Run(async () =>
        {
            try
            {
                await SaveAsync(_stop.Token);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Background save failed.");
            }
        });

        return true;
    }

    /// <summary>Starts periodic saves if <c>SaveIntervalSeconds</c> is set.</summary>
    public void StartPeriodicSaves()
    {
        int seconds = settings.Persistence.SaveIntervalSeconds;
        if (seconds <= 0 || _periodicSaves is not null)
        {
            return;
        }

        _periodicSaves = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), timeProvider);
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token))
                {
                    try
                    {
                        await SaveAsync(_stop.Token);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        logger.LogError(e, "Periodic save failed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
        });
    }

    /// <summary>Stops periodic saves and waits for running ones.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        await _stop.CancelAsync();
        foreach (Task? task in new[] { _periodicSaves, _backgroundSave })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Cancelled by shutdown.
            }
        }

        _stop.Dispose();
    }
}
