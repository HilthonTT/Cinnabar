using Microsoft.Extensions.Time.Testing;

namespace Cinnabar.Storage.Tests;

/// <summary>Base class: a keyspace on a fake clock with the background sweep disabled.</summary>
public abstract class KeyspaceTestBase : IDisposable
{
    protected KeyspaceTestBase()
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Keyspace = new Keyspace(Time, TimeSpan.Zero);
    }

    protected FakeTimeProvider Time { get; }

    protected Keyspace Keyspace { get; }

    protected long NowMs => Time.GetUtcNow().ToUnixTimeMilliseconds();

    protected static IReadOnlyList<KeyValuePair<string, string>> Fields(params string[] pairs)
    {
        var fields = new List<KeyValuePair<string, string>>();
        for (int i = 0; i < pairs.Length; i += 2)
        {
            fields.Add(new KeyValuePair<string, string>(pairs[i], pairs[i + 1]));
        }

        return fields;
    }

    public void Dispose()
    {
        Keyspace.Dispose();
        GC.SuppressFinalize(this);
    }
}
