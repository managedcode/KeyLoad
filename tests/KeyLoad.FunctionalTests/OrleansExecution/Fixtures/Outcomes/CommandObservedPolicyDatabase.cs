using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using P = KeyLoad.FunctionalTests.OrleansExecution.CommandObservedPolicyProtocol;

namespace KeyLoad.FunctionalTests.OrleansExecution;

/// <summary>Uses the execution source connection, canonical outcomes and coordinator without owning storage.</summary>
internal sealed class CommandObservedPolicyDatabase(Func<RequestCqrsHostedRuntimeFixture> source)
{
    internal DatabaseEngine Database => source().Database.Database;
    internal ZoneTreeStore Store => source().Database.Store;
    internal PartitionRef Partition { get; } = P.Partition;
    internal ICommitCoordinator Coordinator => source().Database.Coordinator;

    internal (long AppliedPosition, long StorePosition) ReadCanonicalCut()
        => source().Database.ReadCanonicalCut();

    internal StoredOutcome ReadStoredOutcome(Guid commandId)
        => OutcomeStoreOracle.ReadStored(Database, commandId, TestContext.Current!.Execution.CancellationToken);

    internal async Task<GrainOperationReply> SubmitAsync<T>(OperationKind kind, T payload,
        CancellationToken token, string principal = P.Root, Guid? id = null, Guid? requestId = null)
    {
        token.ThrowIfCancellationRequested();
        var actual = source();
        var commandId = id ?? Guid.NewGuid();
        var operationId = requestId ?? Guid.NewGuid();
        var subject = Store.Read(view => Database.Principal(view, principal, actual.Clock.GetUtcNow()));
        var envelope = actual.Codec.CreateCommand(operationId, subject.Id, kind, commandId,
            OrleansSerialization.Serialize(payload));
        var failures = new List<Exception>();
        GrainOperationReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = new GrainRequestIdentityScope(actual.Services, subject, operationId,
                commandId, token, connectionId: actual.ConnectionId);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reply = await GrainRequestStreamConsumer.DrainAsync(
                    createStream: original => actual.Grains.GetGrain<IConnectionGrain>(actual.ConnectionId)
                        .ExecuteStreamAsync(envelope, original),
                    serializer: actual.Services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
                    requestId: operationId, clock: actual.Clock, options: actual.RoutingOptions,
                    cancellationToken: token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw new InvalidOperationException(P.WrongReply);
    }
}
