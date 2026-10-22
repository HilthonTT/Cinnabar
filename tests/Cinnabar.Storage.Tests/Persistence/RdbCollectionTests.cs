using System.Buffers.Binary;
using System.Text;
using Cinnabar.Storage.Persistence;

namespace Cinnabar.Storage.Tests.Persistence;

/// <summary>Hashes, sets and sorted sets in RDB files.</summary>
public class RdbCollectionTests
{
    private static KeySnapshot RoundTripOne(KeySnapshot key)
    {
        byte[] bytes = RdbWriter.Write([key], out RdbWriteResult result);
        result.Should().Be(new RdbWriteResult(1, 0));
        return RdbReader.Read(bytes).Databases[0].Single();
    }

    private static KeySnapshot ReadOne(byte[] bytes) => RdbReader.Read(bytes).Databases[0].Single();

    [Fact]
    public void Hash_round_trips()
    {
        KeyValuePair<string, string>[] fields = [new("name", "ada"), new("lang", "c#"), new("empty", "")];

        KeySnapshot loaded = RoundTripOne(new KeySnapshot("user", KeyType.Hash, fields, null));

        loaded.Type.Should().Be(KeyType.Hash);
        ((IEnumerable<KeyValuePair<string, string>>)loaded.Value).Should().BeEquivalentTo(fields);
    }

    [Fact]
    public void Set_round_trips()
    {
        string[] members = ["a", "b", "c"];

        KeySnapshot loaded = RoundTripOne(new KeySnapshot("tags", KeyType.Set, members, null));

        loaded.Type.Should().Be(KeyType.Set);
        ((IEnumerable<string>)loaded.Value).Should().BeEquivalentTo(members);
    }

    [Fact]
    public void Sorted_set_round_trips_with_exact_scores()
    {
        ScoredMember[] members =
        [
            new("low", double.NegativeInfinity),
            new("pi", Math.PI),
            new("tiny", double.Epsilon),
            new("big", 1e300),
            new("high", double.PositiveInfinity),
        ];

        KeySnapshot loaded = RoundTripOne(new KeySnapshot("z", KeyType.SortedSet, members, null));

        loaded.Type.Should().Be(KeyType.SortedSet);
        ((IEnumerable<ScoredMember>)loaded.Value).Should().Equal(members);
    }

    [Fact]
    public void Collection_expiry_round_trips()
    {
        DateTimeOffset expiry = DateTimeOffset.FromUnixTimeMilliseconds(1_900_000_000_000);

        KeySnapshot loaded = RoundTripOne(new KeySnapshot("s", KeyType.Set, new[] { "x" }, expiry));

        loaded.ExpiresAt.Should().Be(expiry);
    }

    [Fact]
    public void Only_streams_are_skipped()
    {
        KeySnapshot[] keys =
        [
            new("h", KeyType.Hash, new KeyValuePair<string, string>[] { new("f", "v") }, null),
            new("s", KeyType.Set, new[] { "m" }, null),
            new("z", KeyType.SortedSet, new ScoredMember[] { new("m", 1) }, null),
            new("x", KeyType.Stream, Array.Empty<StreamEntry>(), null),
        ];

        RdbWriter.Write(keys, out RdbWriteResult result);

        result.Should().Be(new RdbWriteResult(3, 1));
    }

    [Fact]
    public void Reads_hand_built_set_and_hash()
    {
        byte[] bytes = new RdbBytes().SelectDb(0)
            .Raw(2).Str("s").Raw(2).Str("a").Str("b")
            .Raw(4).Str("h").Raw(1).Str("f").Str("v")
            .EndWithRedisChecksum();

        List<KeySnapshot> keys = RdbReader.Read(bytes).Databases[0];

        ((IEnumerable<string>)keys[0].Value).Should().Equal("a", "b");
        ((IEnumerable<KeyValuePair<string, string>>)keys[1].Value).Should().BeEquivalentTo([new KeyValuePair<string, string>("f", "v")]);
    }

    [Fact]
    public void Reads_sorted_set_with_binary_scores()
    {
        var score = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(score, 2.5);
        byte[] bytes = new RdbBytes().SelectDb(0).Raw(5).Str("z").Raw(1).Str("m").Raw(score).EndWithZeroChecksum();

        ((IEnumerable<ScoredMember>)ReadOne(bytes).Value).Should().Equal(new ScoredMember("m", 2.5));
    }

    [Fact]
    public void Reads_sorted_set_with_text_scores()
    {
        byte[] bytes = new RdbBytes().SelectDb(0)
            .Raw(3).Str("z").Raw(3)
            .Str("a").Raw(4).Raw(Encoding.ASCII.GetBytes("-1.5"))
            .Str("b").Raw(254)
            .Str("c").Raw(255)
            .EndWithZeroChecksum();

        ((IEnumerable<ScoredMember>)ReadOne(bytes).Value).Should().Equal(
            new ScoredMember("a", -1.5),
            new ScoredMember("b", double.PositiveInfinity),
            new ScoredMember("c", double.NegativeInfinity));
    }

    [Fact]
    public void Nan_score_is_rejected()
    {
        byte[] bytes = new RdbBytes().SelectDb(0).Raw(3).Str("z").Raw(1).Str("a").Raw(253).EndWithZeroChecksum();

        Action act = () => RdbReader.Read(bytes);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Loaded_collections_are_usable_in_a_keyspace()
    {
        using var keyspace = new Keyspace(evictionInterval: TimeSpan.Zero);
        keyspace.HashSetFields("h", [new("f", "v")]);
        keyspace.SetAdd("s", ["a", "b"]);
        keyspace.SortedSetAdd("z", [new("m", 3), new("n", 1)]);

        byte[] bytes = RdbWriter.Write(keyspace.Snapshot(), out _);
        using var restored = new Keyspace(evictionInterval: TimeSpan.Zero);
        restored.Load(RdbReader.Read(bytes).Databases[0]);

        restored.HashGet("h", "f").Should().Be("v");
        restored.SetMembers("s").Should().BeEquivalentTo(["a", "b"]);
        restored.SortedSetRangeByRank("z", 0, -1).Select(m => m.Member).Should().Equal("n", "m");
    }
}
