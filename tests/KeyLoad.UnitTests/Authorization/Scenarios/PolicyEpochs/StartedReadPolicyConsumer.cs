using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class StartedReadPolicyConsumer
{
    internal static async Task<StartedReadPolicyObservation> RunAsync(ConnectionNativeScenario scenario,
        PrincipalRecord principal, Guid requestId, TaskCompletionSource started,
        CancellationToken selected, CancellationToken parent)
    {
        var fixture = scenario.Fixture;
        var signed = fixture.Codec.CreateRead(requestId, principal.Id, GrainReadKind.Document,
            NativeSerialization.Serialize(new GetDocumentRequest(scenario.Reference(StartedReadPolicyProtocol.Document))));
        var frames = new List<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>();
        OperationCanceledException? cancellation = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = new GrainRequestIdentityScope(fixture.Cluster.ServiceProvider, principal,
                requestId, Guid.Empty, selected, connectionId: fixture.ConnectionId);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var bounded = NativeCqrsStreamLifetime.RunWithPurpose(
                    token => scenario.Connection.ExecuteStreamAsync(signed, token).WithBatchSize(GrainRequestStreamProtocol.BatchSize),
                    fixture.Cluster.ServiceProvider.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
                    requestId, fixture.Clock, static () => { }, fixture.RoutingOptions, null,
                    new NativeCqrsStreamPurpose(), selected);
                await using var enumerator = bounded.GetAsyncEnumerator(selected);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    try
                    {
                        while (await enumerator.MoveNextAsync())
                        {
                            var frame = enumerator.Current;
                            frames.Add(frame);
                            if (frame.Kind == CqrsStreamChunkKind.Started)
                            {
                                started.TrySetResult();
                            }
                        }
                    }
                    catch (OperationCanceledException original) when (selected.IsCancellationRequested && !parent.IsCancellationRequested)
                    {
                        cancellation = original;
                    }
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return new(frames, cancellation);
    }
}
