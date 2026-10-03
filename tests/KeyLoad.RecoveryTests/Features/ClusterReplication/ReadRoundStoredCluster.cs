using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests;

internal sealed class ReadRoundStoredCluster : IAsyncDisposable
{
    internal const string VoterA = "read-round-a";
    internal const string VoterB = "read-round-b";
    private const string VoterC = "read-round-c";
    private const string Prefix = "keyload-read-round-";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);
    private readonly string directory = ReplicaFixturePaths.NewDirectory(Prefix);
    private readonly List<ReadRoundStoredNode> nodes = [];
    private bool disposed;

    internal ReadRoundStoredCluster(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 3);
        string[] voters = [VoterA, VoterB, VoterC];
        voters = voters[..count];
        var incarnation = Guid.NewGuid();
        try
        {
            foreach (var voter in voters)
            {
                nodes.Add(new(Path.Combine(directory, voter), voter, voters, incarnation));
            }
        }
        catch (Exception error)
        {
            List<Exception> failures = [error];
            foreach (var node in nodes)
            {
                try
                {
                    ReplicaMaterializerLifecycleErrors.Attempt(() => node.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures);
                }
                catch (AggregateException cleanup) { ReplicaMaterializerLifecycleErrors.Add(cleanup, failures); }
            }
            ReplicaMaterializerLifecycleErrors.Attempt(DeleteDirectory, failures);
            ReplicaMaterializerLifecycleErrors.Throw(failures);
            throw;
        }
    }

    internal IReadOnlyList<ReadRoundStoredNode> Nodes => nodes;

    internal void Attach()
    {
        var peers = nodes.ToDictionary(node => node.Configuration.LocalId, StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            node.Attach(peers);
        }
    }

    internal async Task<ReadRoundStoredNode> ReadyLeaderAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var node in nodes)
            {
                if (await node.Consensus.IsLeaderAsync(cancellationToken))
                {
                    return node;
                }
            }
            await Task.Delay(PollInterval, TimeProvider.System, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        List<Exception> failures = [];
        foreach (var node in nodes)
        {
            try
            {
                await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => node.Consensus.StopAsync(CancellationToken.None), failures);
            }
            catch (AggregateException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        }
        foreach (var node in nodes)
        {
            try
            {
                await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => node.DisposeAsync().AsTask(), failures);
            }
            catch (AggregateException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        }
        ReplicaMaterializerLifecycleErrors.Attempt(DeleteDirectory, failures);
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    private void DeleteDirectory()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
