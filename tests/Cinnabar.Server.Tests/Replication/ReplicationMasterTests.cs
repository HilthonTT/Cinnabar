using Cinnabar.Server.Commands;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Replication;
using Cinnabar.Server.Tests.Infrastructure;

namespace Cinnabar.Server.Tests.Replication;

/// <remarks>
/// Replicas here are socketless sessions: <see cref="ClientSession.SendAsync"/> is a no-op
/// without a socket, so a full sync only registers the replica. The wire protocol is covered
/// by the integration tests.
/// </remarks>
public sealed class ReplicationMasterTests : DispatcherTestBase
{
    private ReplicationMaster Master => H.Master;

    private async Task<ClientSession> ConnectReplica()
    {
        ClientSession session = H.NewSession();
        await Master.StartFullSyncAsync(session, CancellationToken.None);
        return session;
    }

    [Fact]
    public void Replication_id_is_40_lower_case_hex_characters()
    {
        Master.ReplicationId.Should().MatchRegex("^[0-9a-f]{40}$");
    }

    [Fact]
    public void Offset_starts_at_zero()
    {
        Master.Offset.Should().Be(0);
    }

    [Fact]
    public void Propagate_adds_encoded_length_and_returns_new_offset()
    {
        var first = new Command("SET", "k", "v");
        var second = new Command("DEL", "k");

        long afterFirst = Master.Propagate(first);
        long afterSecond = Master.Propagate(second);

        afterFirst.Should().Be(first.Encode().Length);
        afterSecond.Should().Be(first.Encode().Length + second.Encode().Length);
        Master.Offset.Should().Be(afterSecond);
    }

    [Fact]
    public async Task Concurrent_propagation_sums_all_lengths()
    {
        var command = new Command("INCR", "n");
        int length = command.Encode().Length;
        ReplicationMaster master = Master;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                master.Propagate(command);
            }
        })));

        master.Offset.Should().Be(800L * length);
    }

    [Fact]
    public async Task Full_sync_registers_replica_and_marks_session()
    {
        ClientSession replica = await ConnectReplica();

        replica.IsReplica.Should().BeTrue();
        Master.Replicas.Should().ContainSingle().Which.Session.Should().BeSameAs(replica);
    }

    [Fact]
    public async Task Replica_starts_with_ack_at_sync_offset()
    {
        Master.Propagate(new Command("SET", "a", "1"));

        await ConnectReplica();

        ConnectedReplica replica = Master.Replicas.Single();
        replica.StartOffset.Should().Be(Master.Offset);
        replica.AckOffset.Should().Be(Master.Offset);
    }

    [Fact]
    public async Task CountAcknowledged_counts_replicas_at_or_past_offset()
    {
        ClientSession a = await ConnectReplica();
        ClientSession b = await ConnectReplica();
        long offset = Master.Propagate(new Command("SET", "k", "v"));

        Master.CountAcknowledged(offset).Should().Be(0);

        Master.Acknowledge(a, offset);
        Master.CountAcknowledged(offset).Should().Be(1);

        Master.Acknowledge(b, offset + 10);
        Master.CountAcknowledged(offset).Should().Be(2);
    }

    [Fact]
    public async Task Acknowledge_only_moves_forward()
    {
        ClientSession session = await ConnectReplica();

        Master.Acknowledge(session, 100);
        Master.Acknowledge(session, 50);

        Master.Replicas.Single().AckOffset.Should().Be(100);
    }

    [Fact]
    public void Acknowledge_from_unknown_session_is_ignored()
    {
        Master.Acknowledge(H.NewSession(), 100);

        Master.Replicas.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveReplica_forgets_it()
    {
        ClientSession session = await ConnectReplica();

        Master.RemoveReplica(session);

        Master.Replicas.Should().BeEmpty();
        Master.CountAcknowledged(0).Should().Be(0);
    }

    [Fact]
    public async Task WaitForReplicas_without_replicas_returns_zero_immediately()
    {
        int result = await Master.WaitForReplicasAsync(3, 100, timeout: null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        result.Should().Be(0);
    }

    [Fact]
    public async Task WaitForReplicas_returns_once_enough_replicas_acknowledge()
    {
        ClientSession session = await ConnectReplica();
        long offset = Master.Propagate(new Command("SET", "k", "v"));

        Task<int> wait = Master.WaitForReplicasAsync(1, offset, TimeSpan.FromSeconds(10), CancellationToken.None);
        Master.Acknowledge(session, Master.Offset);

        (await wait.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(1);
    }

    [Fact]
    public async Task WaitForReplicas_sends_getack_to_replicas()
    {
        await ConnectReplica();
        long offset = Master.Propagate(new Command("SET", "k", "v"));

        await Master.WaitForReplicasAsync(1, offset, TimeSpan.FromMilliseconds(30), CancellationToken.None);

        Master.Offset.Should().Be(offset + new Command("REPLCONF", "GETACK", "*").Encode().Length);
    }

    [Fact]
    public async Task WaitForReplicas_times_out_with_current_count()
    {
        await ConnectReplica();
        long offset = Master.Propagate(new Command("SET", "k", "v"));

        int result = await Master.WaitForReplicasAsync(1, offset, TimeSpan.FromMilliseconds(30), CancellationToken.None);

        result.Should().Be(0);
    }

    [Fact]
    public async Task WaitForReplicas_is_cancellable()
    {
        await ConnectReplica();
        long offset = Master.Propagate(new Command("SET", "k", "v"));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        Func<Task> act = () => Master.WaitForReplicasAsync(1, offset, timeout: null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
