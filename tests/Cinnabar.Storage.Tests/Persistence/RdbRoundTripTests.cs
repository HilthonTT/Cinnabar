using Cinnabar.Storage.Persistence;

namespace Cinnabar.Storage.Tests.Persistence;

public class RdbRoundTripTests
{
    private static List<KeySnapshot> RoundTrip(IEnumerable<KeySnapshot> keys, out RdbWriteResult result)
    {
        byte[] bytes = RdbWriter.Write(keys, out result);
        RdbSnapshot snapshot = RdbReader.Read(bytes);
        return snapshot.Databases.TryGetValue(0, out List<KeySnapshot>? loaded) ? loaded : [];
    }

    private static List<KeySnapshot> RoundTrip(params KeySnapshot[] keys) => RoundTrip(keys, out _);

    [Fact]
    public void Writes_redis_header_version_9()
    {
        byte[] bytes = RdbWriter.Write([], out _);

        System.Text.Encoding.ASCII.GetString(bytes, 0, 9).Should().Be("REDIS0009");
        RdbReader.Read(bytes).Version.Should().Be(9);
    }

    [Fact]
    public void Empty_snapshot_round_trips()
    {
        RoundTrip().Should().BeEmpty();
    }

    [Fact]
    public void Writes_aux_metadata()
    {
        RdbSnapshot snapshot = RdbReader.Read(RdbWriter.Write([], out _));

        snapshot.Metadata.Select(m => m.Key).Should().Contain(["cinnabar-ver", "ctime"]);
    }

    [Fact]
    public void String_round_trips()
    {
        KeySnapshot loaded = RoundTrip(new KeySnapshot("greeting", KeyType.String, "hello", null)).Single();

        loaded.Key.Should().Be("greeting");
        loaded.Type.Should().Be(KeyType.String);
        loaded.Value.Should().Be("hello");
        loaded.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void Empty_string_round_trips()
    {
        RoundTrip(new KeySnapshot("k", KeyType.String, "", null)).Single().Value.Should().Be("");
    }

    [Fact]
    public void List_round_trips_in_order()
    {
        KeySnapshot loaded = RoundTrip(new KeySnapshot("l", KeyType.List, new[] { "a", "b", "c" }, null)).Single();

        loaded.Type.Should().Be(KeyType.List);
        ((IEnumerable<string>)loaded.Value).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Expiry_round_trips_with_millisecond_precision()
    {
        DateTimeOffset expiry = DateTimeOffset.FromUnixTimeMilliseconds(1_900_000_000_123);

        KeySnapshot loaded = RoundTrip(
            new KeySnapshot("s", KeyType.String, "v", expiry),
            new KeySnapshot("l", KeyType.List, new[] { "x" }, expiry)).First();

        loaded.ExpiresAt.Should().Be(expiry);
    }

    [Fact]
    public void Expiry_applies_only_to_following_key()
    {
        DateTimeOffset expiry = DateTimeOffset.FromUnixTimeMilliseconds(1_900_000_000_000);

        List<KeySnapshot> loaded = RoundTrip(
            new KeySnapshot("a", KeyType.String, "v", expiry),
            new KeySnapshot("b", KeyType.String, "v", null));

        loaded.Single(k => k.Key == "a").ExpiresAt.Should().Be(expiry);
        loaded.Single(k => k.Key == "b").ExpiresAt.Should().BeNull();
    }

    [Theory]
    [InlineData("日本語のキー", "värde €")]
    [InlineData("emoji🎉", "🎉🎊")]
    public void Unicode_round_trips(string key, string value)
    {
        // Stored strings are byte strings: the UTF-8 bytes of the text, one per char.
        string keyBytes = ByteString.FromText(key);
        string valueBytes = ByteString.FromText(value);

        KeySnapshot loaded = RoundTrip(new KeySnapshot(keyBytes, KeyType.String, valueBytes, null)).Single();

        loaded.Key.Should().Be(keyBytes);
        loaded.Value.Should().Be(valueBytes);
        ByteString.ToText((string)loaded.Value).Should().Be(value);
    }

    [Fact]
    public void Binary_values_round_trip_byte_for_byte()
    {
        byte[] payload = [.. Enumerable.Range(0, 256).Select(i => (byte)i), 0xC3, 0x28, 0xFF, 0x00];
        string value = ByteString.FromBytes(payload);

        KeySnapshot loaded = RoundTrip(new KeySnapshot("bin", KeyType.String, value, null)).Single();

        ByteString.GetBytes((string)loaded.Value).Should().Equal(payload);
    }

    [Theory]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(16_383)]
    [InlineData(16_384)]
    [InlineData(100_000)]
    public void Strings_at_every_length_encoding_boundary_round_trip(int length)
    {
        string value = new string('x', length - 1) + "!";

        RoundTrip(new KeySnapshot("k", KeyType.String, value, null)).Single().Value.Should().Be(value);
    }

    [Fact]
    public void List_with_many_items_round_trips()
    {
        string[] items = Enumerable.Range(0, 20_000).Select(i => i.ToString()).ToArray();

        KeySnapshot loaded = RoundTrip(new KeySnapshot("l", KeyType.List, items, null)).Single();

        ((IEnumerable<string>)loaded.Value).Should().Equal(items);
    }

    [Fact]
    public void Stream_entries_round_trip_with_expiry()
    {
        DateTimeOffset expiry = DateTimeOffset.FromUnixTimeMilliseconds(4_102_444_800_000);
        StreamEntry[] entries =
        [
            new(new StreamId(1, 1), [new KeyValuePair<string, string>("f", "v"), new KeyValuePair<string, string>("g", "\u00ff\u0000")]),
            new(new StreamId(2, 0), [new KeyValuePair<string, string>("f", "w")]),
        ];

        List<KeySnapshot> loaded = RoundTrip(
            [
                new KeySnapshot("s", KeyType.String, "v", null),
                new KeySnapshot("x", KeyType.Stream, entries, expiry),
            ],
            out RdbWriteResult result);

        result.Should().Be(new RdbWriteResult(WrittenKeys: 2, SkippedKeys: 0));
        KeySnapshot stream = loaded.Single(k => k.Key == "x");
        stream.ExpiresAt.Should().Be(expiry);
        var snapshot = (StreamSnapshot)stream.Value;
        snapshot.Entries.Select(e => e.Id).Should().Equal(new StreamId(1, 1), new StreamId(2, 0));
        snapshot.Entries[0].Fields.Should().Equal(entries[0].Fields);
        snapshot.Groups.Should().BeEmpty();
    }

    [Fact]
    public void Stream_consumer_groups_round_trip()
    {
        StreamEntry[] entries = [new(new StreamId(5, 0), [new KeyValuePair<string, string>("f", "v")])];
        StreamGroupSnapshot group = new(
            "g",
            new StreamId(5, 0),
            [new StreamConsumerSnapshot("alice", 1000), new StreamConsumerSnapshot("bob", 2000)],
            [new StreamPendingSnapshot(new StreamId(5, 0), "bob", 1500, 3)]);

        KeySnapshot loaded = RoundTrip(new KeySnapshot("x", KeyType.Stream, new StreamSnapshot(entries, [group]), null)).Single();

        var snapshot = (StreamSnapshot)loaded.Value;
        StreamGroupSnapshot restored = snapshot.Groups.Single();
        restored.Name.Should().Be("g");
        restored.LastDeliveredId.Should().Be(new StreamId(5, 0));
        restored.Consumers.Should().Equal(group.Consumers);
        restored.Pending.Should().Equal(group.Pending);
    }

    [Fact]
    public void Empty_stream_with_a_group_round_trips()
    {
        StreamGroupSnapshot group = new("g", StreamId.Min, [], []);

        KeySnapshot loaded = RoundTrip(new KeySnapshot("x", KeyType.Stream, new StreamSnapshot([], [group]), null)).Single();

        ((StreamSnapshot)loaded.Value).Groups.Single().Name.Should().Be("g");
    }

    [Fact]
    public void Keyspace_streams_and_groups_survive_rdb()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        using var source = new Keyspace(time, TimeSpan.Zero);
        source.StreamAdd("s", "1-1", [new KeyValuePair<string, string>("f", "a")]);
        source.StreamAdd("s", "1-2", [new KeyValuePair<string, string>("f", "b")]);
        source.StreamGroupCreate("s", "g", "0");
        source.StreamReadGroup("s", "g", "alice", null, count: 1);

        byte[] bytes = RdbWriter.Write(source.Snapshot(), out _);
        using var target = new Keyspace(time, TimeSpan.Zero);
        target.Load(RdbReader.Read(bytes).Databases[0]);

        target.StreamLength("s").Should().Be(2);
        StreamPendingSummary pending = target.StreamPending("s", "g");
        pending.Count.Should().Be(1);
        pending.Consumers.Should().ContainSingle().Which.Key.Should().Be("alice");

        // The group resumes where it left off: the next new entry is 1-2.
        target.StreamReadGroup("s", "g", "bob", null, count: 10).Single().Id.Should().Be(new StreamId(1, 2));
    }

    [Fact]
    public void Keyspace_snapshot_round_trips_through_rdb()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        using var source = new Keyspace(time, TimeSpan.Zero);
        source.SetString("s", "v", expiry: TimeSpan.FromMinutes(5));
        source.ListPush("l", ["a", "b"], ListEnd.Right);
        source.IncrementBy("n", 42);

        byte[] bytes = RdbWriter.Write(source.Snapshot(), out _);
        using var target = new Keyspace(time, TimeSpan.Zero);
        target.Load(RdbReader.Read(bytes).Databases[0]).Should().Be(3);

        target.GetString("s").Should().Be("v");
        target.GetTimeToLiveMilliseconds("s").Should().Be(300_000);
        target.ListRange("l", 0, -1).Should().Equal("a", "b");
        target.GetString("n").Should().Be("42");
    }

    [Fact]
    public async Task WriteFileAsync_then_ReadFileAsync_round_trips()
    {
        string directory = Path.Combine(Path.GetTempPath(), "cinnabar-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "nested", "dump.rdb");
        try
        {
            RdbWriteResult result = await RdbWriter.WriteFileAsync(path, [new KeySnapshot("k", KeyType.String, "v", null)]);

            result.WrittenKeys.Should().Be(1);
            File.Exists(path).Should().BeTrue();
            File.Exists(path + ".tmp").Should().BeFalse("the temporary file is renamed into place");
            Directory.GetFiles(Path.GetDirectoryName(path)!).Should().ContainSingle();

            RdbSnapshot snapshot = await RdbReader.ReadFileAsync(path);
            snapshot.Databases[0].Single().Value.Should().Be("v");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteFileAsync_replaces_existing_file()
    {
        string directory = Path.Combine(Path.GetTempPath(), "cinnabar-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "dump.rdb");
        try
        {
            await RdbWriter.WriteFileAsync(path, [new KeySnapshot("old", KeyType.String, "1", null)]);
            await RdbWriter.WriteFileAsync(path, [new KeySnapshot("new", KeyType.String, "2", null)]);

            RdbSnapshot snapshot = await RdbReader.ReadFileAsync(path);
            snapshot.Databases[0].Select(k => k.Key).Should().Equal("new");
            Directory.GetFiles(directory).Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
