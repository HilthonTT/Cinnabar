using System.Collections.Concurrent;

namespace Cinnabar.Storage.Tests;

public class KeyspaceConcurrencyTests
{
    private static readonly ParallelOptions Parallelism = new() { MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount) };

    [Fact]
    public void Parallel_increments_on_one_key_are_not_lost()
    {
        using var keyspace = new Keyspace(evictionInterval: TimeSpan.Zero);

        Parallel.For(0, 20_000, Parallelism, _ => keyspace.IncrementBy("counter", 1));

        keyspace.GetString("counter").Should().Be("20000");
    }

    [Fact]
    public void Parallel_increments_on_many_keys_are_not_lost()
    {
        using var keyspace = new Keyspace(evictionInterval: TimeSpan.Zero);

        Parallel.For(0, 20_000, Parallelism, i => keyspace.IncrementBy($"counter:{i % 50}", 2));

        Enumerable.Range(0, 50).Select(i => keyspace.GetString($"counter:{i}")).Should().AllBe("800");
    }

    [Fact]
    public void Parallel_pushes_keep_every_element()
    {
        using var keyspace = new Keyspace(evictionInterval: TimeSpan.Zero);

        Parallel.For(0, 10_000, Parallelism, i =>
            keyspace.ListPush("l", [i.ToString()], i % 2 == 0 ? ListEnd.Left : ListEnd.Right));

        keyspace.ListLength("l").Should().Be(10_000);
        keyspace.ListRange("l", 0, -1).Select(int.Parse).Order().Should().Equal(Enumerable.Range(0, 10_000));
    }

    [Fact]
    public void Parallel_pops_conserve_elements()
    {
        using var keyspace = new Keyspace(evictionInterval: TimeSpan.Zero);
        const int total = 10_000;
        keyspace.ListPush("l", Enumerable.Range(0, total).Select(i => i.ToString()), ListEnd.Right);
        var popped = new ConcurrentBag<string>();

        Parallel.For(0, total + 500, Parallelism, i =>
        {
            foreach (string value in keyspace.ListPop("l", i % 2 == 0 ? ListEnd.Left : ListEnd.Right))
            {
                popped.Add(value);
            }
        });

        popped.Should().HaveCount(total);
        popped.Select(int.Parse).Order().Should().Equal(Enumerable.Range(0, total));
        keyspace.Exists("l").Should().BeFalse();
    }

    [Fact]
    public void Concurrent_pushes_and_pops_conserve_elements()
    {
        using var keyspace = new Keyspace(evictionInterval: TimeSpan.Zero);
        const int total = 10_000;
        var popped = new ConcurrentBag<string>();

        Parallel.For(0, total * 2, Parallelism, i =>
        {
            if (i % 2 == 0)
            {
                keyspace.ListPush("l", [(i / 2).ToString()], ListEnd.Right);
            }
            else
            {
                foreach (string value in keyspace.ListPop("l", ListEnd.Left))
                {
                    popped.Add(value);
                }
            }
        });

        IEnumerable<string> remaining = keyspace.ListRange("l", 0, -1);
        popped.Concat(remaining).Select(int.Parse).Order().Should().Equal(Enumerable.Range(0, total));
    }

    [Fact]
    public void Parallel_stream_adds_with_star_get_unique_increasing_ids()
    {
        using var keyspace = new Keyspace(evictionInterval: TimeSpan.Zero);
        var ids = new ConcurrentBag<StreamId>();

        Parallel.For(0, 5_000, Parallelism, i =>
            ids.Add(keyspace.StreamAdd("s", "*", [new KeyValuePair<string, string>("i", i.ToString())])));

        ids.Distinct().Should().HaveCount(5_000);
        keyspace.StreamRange("s", StreamId.Min, StreamId.Max).Select(e => e.Id).Should().BeInAscendingOrder();
        keyspace.StreamLength("s").Should().Be(5_000);
    }
}
