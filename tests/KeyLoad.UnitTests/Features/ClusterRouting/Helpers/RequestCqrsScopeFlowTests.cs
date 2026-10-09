using System.Security.Claims;
using KeyLoad.Orleans;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Identity.Core.Constants;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsScopeFlowCases
{
    private const string PersistedPrefix = "scope-flow-subject-";

    internal static async Task AcCrs006ConcurrentPersistedScopesStayIsolatedThroughNativeCalls(
        RequestCqrsClusterFixture fixture)
    {
        var first = PersistPrincipal(fixture, PersistedPrefix + Guid.NewGuid().ToString("N"));
        var second = PersistPrincipal(fixture, PersistedPrefix + Guid.NewGuid().ToString("N"));
        var bothReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        Action arrive = () =>
        {
            if (Interlocked.Increment(ref arrivals) == 2)
            {
                bothReady.TrySetResult();
            }
        };
        var firstCall = ValidateConcurrentAsync(fixture, first, bothReady, arrive);
        var secondCall = ValidateConcurrentAsync(fixture, second, bothReady, arrive);
        var calls = await Task.WhenAll(firstCall, secondCall);
        var firstResult = calls[0];
        var secondResult = calls[1];
        await Assert.That(firstResult.Result.Subject).IsEqualTo(first.Id);
        await Assert.That(firstResult.Result.CommandId).IsEqualTo(firstResult.CommandId);
        await Assert.That(secondResult.Result.Subject).IsEqualTo(second.Id);
        await Assert.That(secondResult.Result.CommandId).IsEqualTo(secondResult.CommandId);
        await Assert.That(firstResult.Result.RequestId).IsNotEqualTo(secondResult.Result.RequestId);
        await Assert.That(Volatile.Read(ref arrivals)).IsEqualTo(2);
    }

    internal static async Task AcCrs006PersistedScopeLivesThroughActualConsumerDisposal(
        RequestCqrsClusterFixture fixture)
    {
        var principal = PersistPrincipal(fixture, PersistedPrefix + Guid.NewGuid().ToString("N"));
        var requestId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var userSlot = RequestCqrsContextSlot.Capture(OrleansIdentityConstants.USER_CLAIMS);
        var stateSlot = RequestCqrsContextSlot.Capture(GrainRequestStreamProtocol.ContextKey);
        try
        {
            var producerSawScope = false;
            using (new GrainRequestIdentityScope(fixture.Cluster.ServiceProvider, principal,
                requestId, commandId, CancellationToken.None, connectionId: fixture.ConnectionId))
            {
                var reply = await GrainRequestStreamConsumer.DrainAsync(
                    token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                        writer => CompleteWhileScopedAsync(writer, requestId, commandId, principal.Id,
                            () => producerSawScope = true, fixture.ConnectionId), token),
                    fixture.Cluster.ServiceProvider.GetRequiredService<Serializer<
                        CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
                    requestId, TimeProvider.System, fixture.RoutingOptions, CancellationToken.None);
                await Assert.That(reply.Payload.IsEmpty).IsFalse();
                await Assert.That(producerSawScope).IsTrue();
                await AssertPublishedContextAsync(requestId, commandId, principal.Id, fixture.ConnectionId);
            }

            await userSlot.AssertOriginalAsync();
            await stateSlot.AssertOriginalAsync();
        }
        finally
        {
            userSlot.Restore();
            stateSlot.Restore();
        }
    }

    private static PrincipalRecord PersistPrincipal(RequestCqrsClusterFixture fixture, string subject)
    {
        var record = new PrincipalRecord(subject, fixture.Database.Partition.TenantId,
            [new ScopeGrant(fixture.Database.Partition.DatabaseId, "*", Capability.All)], ["*"]);
        return fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record))
            .Get<PrincipalRecord>();
    }

    private static async Task<(RequestCqrsProbeResult Result, Guid CommandId)> ValidateConcurrentAsync(
        RequestCqrsClusterFixture fixture, PrincipalRecord principal, TaskCompletionSource bothReady, Action arrive)
    {
        var requestId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        try
        {
            using var scope = new GrainRequestIdentityScope(fixture.Cluster.ServiceProvider, principal,
                requestId, commandId, CancellationToken.None, connectionId: fixture.ConnectionId);
            arrive();
            await bothReady.Task;
            var token = fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.ConfigureResource, commandId,
                NativeSerialization.Serialize(0));
            var result = await fixture.Cluster.Client.GetGrain<IRequestCqrsIdentityProbeGrain>(requestId)
                .ValidateAsync(token, requestId);
            await Assert.That(result.Accepted).IsTrue();
            await Assert.That(result.Subject).IsEqualTo(principal.Id);
            await Assert.That(result.RequestId).IsEqualTo(requestId);
            await Assert.That(result.CommandId).IsEqualTo(commandId);
            return (result, commandId);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            bothReady.TrySetException(error);
            throw;
        }
    }

    private static async ValueTask<Result<GrainOperationReply>> CompleteWhileScopedAsync(
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, Guid requestId, Guid commandId,
        string subject, Action producerCompleted, Guid connectionId)
    {
        await AssertPublishedContextAsync(requestId, commandId, subject, connectionId);
        await writer.StartedAsync(new GrainRequestProgress(requestId));
        try
        {
            await AssertPublishedContextAsync(requestId, commandId, subject, connectionId);
            return Result<GrainOperationReply>.Succeed(new GrainOperationReply { Payload = new byte[] { 1 } });
        }
        finally
        {
            await AssertPublishedContextAsync(requestId, commandId, subject, connectionId);
            producerCompleted();
        }
    }

    private static async Task AssertPublishedContextAsync(Guid requestId, Guid commandId, string subject, Guid connectionId)
    {
        var principal = RequestContext.Get(OrleansIdentityConstants.USER_CLAIMS) as ClaimsPrincipal;
        await Assert.That(principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value).IsEqualTo(subject);
        await Assert.That(RequestContext.Get(GrainRequestStreamProtocol.ContextKey))
            .IsEqualTo(new GrainRequestContextState(requestId, commandId, connectionId));
    }
}
