using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Identity.Core.Constants;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class ConnectionAwaitedSettlementCalls
{
    internal static Task<GrainOperationReply> CommandAsync(ConnectionScenario scenario,
        PrincipalRecord principal, Guid requestId, CommandRequest command, CancellationToken token)
        => CommandForOwnerAsync(scenario, principal, requestId, command, scenario.Fixture.ConnectionId, token);

    internal static async Task<GrainOperationReply> CommandForOwnerAsync(ConnectionScenario scenario,
        PrincipalRecord principal, Guid requestId, CommandRequest command, Guid connectionId, CancellationToken token)
    {
        scenario.Fixture.RegisterConsumer(connectionId);
        var before = CaptureContext();
        Exception? primary = null;
        try
        {
            using var identity = new GrainRequestIdentityScope(scenario.Fixture.Cluster.ServiceProvider,
                principal, requestId, command.CommandId, token, connectionId: connectionId);
            var envelope = scenario.Fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.Batch,
                command.CommandId, OrleansSerialization.Serialize(command));
            return await GrainRequestStreamConsumer.DrainAsync(execution => scenario.Fixture.Cluster.Client.GetGrain<IConnectionGrain>(connectionId).ExecuteStreamAsync(envelope, execution),
                scenario.Fixture.Cluster.ServiceProvider.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
                requestId, scenario.Fixture.Clock, scenario.Fixture.RoutingOptions, token);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            var restoration = (await RequestWorkSettlement.JoinAsync(RequireRestoredAsync(before))).Value;
            if (restoration is not null)
            {
                RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(primary, restoration));
            }
        }
    }

    internal static async Task EarlyDisposeAsync(ConnectionScenario scenario,
        PrincipalRecord principal, Guid requestId, CommandRequest command, CancellationToken token)
    {
        var before = CaptureContext();
        Exception? primary = null;
        try
        {
            using var identity = new GrainRequestIdentityScope(scenario.Fixture.Cluster.ServiceProvider,
                principal, requestId, command.CommandId, token, connectionId: scenario.Fixture.ConnectionId);
            var envelope = scenario.Fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.Batch,
                command.CommandId, OrleansSerialization.Serialize(command));
            await using var iterator = scenario.Connection.ExecuteStreamAsync(envelope, token)
                .WithBatchSize(GrainRequestStreamProtocol.BatchSize).GetAsyncEnumerator(token);
            await Assert.That(await iterator.MoveNextAsync()).IsTrue();
            await Assert.That(iterator.Current.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
            await Assert.That(iterator.Current.ProgressResult!.Value.Value!.RequestId).IsEqualTo(requestId);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            var restoration = (await RequestWorkSettlement.JoinAsync(RequireRestoredAsync(before))).Value;
            if (restoration is not null)
            {
                RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(primary, restoration));
            }
        }
    }

    internal static async Task<global::ManagedCode.Communication.Result<Exception?>> FailureAsync(Task<GrainOperationReply> original)
    {
        try
        {
            await original;
            return global::ManagedCode.Communication.Result<Exception?>.FailNotFound(nameof(FailureAsync));
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error)) { return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error))); }
    }
    internal static async Task JoinOriginalAsync(Task original, Exception? primary,
        bool allowCancellation = false, bool expectedFailure = false)
    {
        var cleanup = (await RequestWorkSettlement.JoinAsync(original)).Value;
        if (allowCancellation && cleanup is OperationCanceledException || expectedFailure && primary is null)
        {
            cleanup = null;
        }
        RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(primary, cleanup));
    }

    private static ContextSnapshot CaptureContext()
        => new(Capture(GrainRequestStreamProtocol.ContextKey), Capture(OrleansIdentityConstants.USER_CLAIMS),
            Capture(global::Orleans.Runtime.Placement.IPlacementDirector.PlacementHintKey));

    private static (bool Present, object? Value) Capture(string key)
        => (RequestContext.Keys.Contains(key, StringComparer.Ordinal), RequestContext.Get(key));

    private static async Task RequireRestoredAsync(ContextSnapshot original)
    {
        var current = CaptureContext();
        await Assert.That(current.State.Present).IsEqualTo(original.State.Present);
        await Assert.That(ReferenceEquals(current.State.Value, original.State.Value)).IsTrue();
        await Assert.That(current.Claims.Present).IsEqualTo(original.Claims.Present);
        await Assert.That(ReferenceEquals(current.Claims.Value, original.Claims.Value)).IsTrue();
        await Assert.That(current.Placement.Present).IsEqualTo(original.Placement.Present);
        await Assert.That(ReferenceEquals(current.Placement.Value, original.Placement.Value)).IsTrue();
    }

    private readonly record struct ContextSnapshot((bool Present, object? Value) State,
        (bool Present, object? Value) Claims, (bool Present, object? Value) Placement);

}
