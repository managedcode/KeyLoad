using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using K = KeyLoad.UnitTests.Features.Search.TextMaintenanceLifetimeNativeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextMaintenanceLifetimeNativeCalls
{
    internal static async Task<TextMaintenanceCapabilityResult> ExecuteAsync(NativeLocalChildLifetimeSession session,
        NativeLocalChildLifetimeOperation operation, NativeLocalChildLifetimeSeeded state,
        TextMaintenanceLifetimeNativeRuntime runtime, TextIndexMaintenanceRequest request,
        TextMaintenanceCapabilityKind phase, ProjectionBatch? page = null,
        CommitProjectionBatchRequest? intent = null, ProjectionBatchResult? receipt = null)
    {
        var input = new TextMaintenanceCapabilityRequest(runtime.SessionId, request, phase, page, intent, receipt);
        var id = Guid.NewGuid();
        var signed = operation.Codec.CreateTextMaintenanceChild(id, P.Root, GrainReadKind.TextMaintenance,
            null, Guid.Empty, NativeSerialization.Serialize(input), operation.Expiry);
        var verified = operation.Codec.VerifyRequest(signed, id);
        var reservation = operation.Parent.ReserveLocalChild(verified, state.PhysicalOwner, state.Leader.Discovery.Read());
        session.Reservations.Add(reservation);
        await UnconsumedAsync(reservation.Child);
        var child = operation.Owner.AcquireVerifiedProducer(verified, state.PhysicalOwner, state.Leader.Discovery.Read());
        await Assert.That(ReferenceEquals(child, reservation.Child)).IsTrue();
        await Assert.That(ReferenceEquals(child.RequireOriginalParentBinding(), operation.Parent)).IsTrue();
        var database = state.Leader.Journal.Database;
        var principal = database.Store.Read(view => database.Principal(view, P.Root, database.EvaluationClock.GetUtcNow()));
        var failures = new List<Exception>();
        TextMaintenanceCapabilityResult? result = null;
        await ServerFailureObserver.ObserveAsync(async () => result = await runtime.Explicit.ExecuteBoundAsync(
            principal, input, operation.Expiry, child, operation.Token), failures);
        var original = failures.FirstOrDefault();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var outcome = await child.CompleteAsync(original, null);
            NativeRequestWorkSettlement.Rethrow(outcome.CleanupOrJoinFailure);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () => _ = await reservation.CompleteDispatchAsync(original, []), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(K.Missing);
    }

    private static async Task UnconsumedAsync(NativeRequestWorkLease child)
    {
        KeyLoadException? observed = null;
        try
        {
            _ = child.RequireOriginalParentBinding();
        }
        catch (KeyLoadException error)
        {
            observed = error;
        }
        await Assert.That(observed?.Code).IsEqualTo(ErrorCode.OwnershipLost);
    }
}
