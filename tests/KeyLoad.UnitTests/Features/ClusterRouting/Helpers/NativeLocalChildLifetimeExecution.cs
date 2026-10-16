using KeyLoad.Clustering;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeExecution
{
    internal static NativeRequestWorkLease Consume(NativeLocalChildLifetimeSeeded state,
        NativeLocalChildLifetimeOperation operation, NativeLocalChildLifetimeInput input)
        => operation.Owner.AcquireVerifiedProducer(input.Verified, state.PhysicalOwner, state.Leader.Discovery.Read());

    internal static PartitionQueryLeafResultV1 Read(NativeLocalChildLifetimeSeeded state,
        NativeLocalChildLifetimeOperation operation, NativeLocalChildLifetimeInput input, NativeRequestWorkLease child)
    {
        var database = state.Leader.Journal.Database;
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        return PartitionQueryLifetimeLeafExecution.ExecuteBound(engine, P.Root, input.Request, state.PhysicalOwner,
            database.EvaluationClock, operation.Expiry, operation.Token, child);
    }

    internal static async Task<PartitionQueryLeafResultV1> HeldProducerAsync(NativeLocalChildLifetimeSession session,
        NativeLocalChildLifetimeSeeded state, NativeLocalChildLifetimeOperation operation,
        NativeLocalChildLifetimeInput input, NativeRequestWorkLease child)
    {
        var result = Read(state, operation, input, child);
        session.ProducerReady.TrySetResult();
        await session.ReleaseProducer.Task.WaitAsync(operation.Token);
        child.AdmitPublication();
        return result;
    }

    internal static async Task AwaitReadyAsync(NativeLocalChildLifetimeSession session,
        Task<PartitionQueryLeafResultV1> original, CancellationToken token)
    {
        var winner = await Task.WhenAny(original, session.ProducerReady.Task).WaitAsync(token);
        if (ReferenceEquals(winner, original))
        {
            _ = await original;
            throw new InvalidOperationException(K.Missing);
        }
        await session.ProducerReady.Task.WaitAsync(token);
    }
}
