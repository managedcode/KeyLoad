using KeyLoad.Clustering;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeRefusals
{
    internal static async Task ExerciseAsync(NativeLocalChildLifetimeSeeded state,
        NativeLocalChildLifetimeOperation operation, NativeLocalChildLifetimeInput original,
        NativeLocalChildLifetimeInput changed)
    {
        var missing = NativeLocalChildLifetimePayload.Sign(operation, original.Request, Guid.NewGuid(), P.Root);
        var wrongBody = NativeLocalChildLifetimePayload.Sign(operation, changed.Request,
            original.Verified.Envelope.RequestId, P.Root);
        var wrongSubject = NativeLocalChildLifetimePayload.Sign(operation, original.Request,
            original.Verified.Envelope.RequestId, K.OtherSubject);
        await Assert.That(wrongBody.Verified.Payload.Span.SequenceEqual(original.Verified.Payload.Span)).IsFalse();
        await Assert.That(wrongSubject.Verified.Payload.Span.SequenceEqual(original.Verified.Payload.Span)).IsTrue();
        await Assert.That(wrongSubject.Verified.Envelope.PrincipalId).IsEqualTo(K.OtherSubject);
        await RefuseAsync(state, operation, missing.Verified, state.Leader);
        await RefuseAsync(state, operation, wrongBody.Verified, state.Leader);
        await RefuseAsync(state, operation, wrongSubject.Verified, state.Leader);
        await RefuseAsync(state, operation, original.Verified, state.Other);
    }

    internal static async Task ReceivingTenantAsync(NativeLocalChildLifetimeSession session,
        NativeLocalChildLifetimeSeeded state, NativeLocalChildLifetimeOperation operation,
        NativeLocalChildLifetimeInput original)
    {
        await Assert.That(original.Request.Principal.ClusterAdministrator).IsTrue();
        await Assert.That(original.Request.Principal.TenantId).IsNotEqualTo(P.Partition.TenantId);
        await Assert.That(original.Request.Original.Tenant).IsEqualTo(original.Request.Principal.TenantId);
        var changed = original.Request with
        {
            Original = original.Request.Original with
            {
                Tenant = P.Partition.TenantId
            }
        };
        await ReadRefusedAsync(session, state, operation, changed, ErrorCode.OwnershipLost,
            "The partition query lifetime request is invalid.");
        changed = changed with
        {
            Principal = changed.Principal with
            {
                TenantId = P.Partition.TenantId
            }
        };
        await ReadRefusedAsync(session, state, operation, changed, ErrorCode.PermissionDenied,
            "The partition query receiving identity changed.");
    }

    private static async Task ReadRefusedAsync(NativeLocalChildLifetimeSession session,
        NativeLocalChildLifetimeSeeded state, NativeLocalChildLifetimeOperation operation,
        PartitionQueryOwnedLeafRequestV2 request, ErrorCode code, string detail)
    {
        var before = operation.Ledger.RetainedEncodedBytes;
        var entries = operation.Ledger.RetainedEntries;
        var input = NativeLocalChildLifetimePayload.Sign(operation, request, Guid.NewGuid(), P.Root);
        var reservation = session.Reserve(operation, state, input);
        var child = NativeLocalChildLifetimeExecution.Consume(state, operation, input);
        var failures = new List<Exception>();
        KeyLoadException? refused = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            PartitionQueryLeafResultV1? published = null;
            refused = Assert.ThrowsExactly<KeyLoadException>(() => published =
                NativeLocalChildLifetimeExecution.Read(state, operation, input, child))
                ?? throw new InvalidOperationException(K.Missing);
            await Assert.That(refused.Code).IsEqualTo(code);
            await Assert.That(refused.Message).IsEqualTo(detail);
            await Assert.That(published).IsNull();
            await NativeLocalChildLifetimeSeed.UnchangedAsync(state.Leader,
                state.Images[state.Leader]);
            await NativeLocalChildLifetimeSeed.UnchangedAsync(state.Other,
                state.Images[state.Other]);
        }, failures);
        NativeRequestWorkOriginalSettlement? outcome = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            outcome = await child.CompleteAsync(refused ?? failures.FirstOrDefault(), null);
            await Assert.That(outcome.OriginalFailure).IsSameReferenceAs(refused);
            await Assert.That(outcome.CleanupFailures.IsEmpty).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var report = await reservation.CompleteDispatchAsync(refused ?? failures.FirstOrDefault(), []);
            await Assert.That(report.OriginalFailure).IsSameReferenceAs(refused);
            await Assert.That(report.ChildOutcome).IsSameReferenceAs(outcome);
            await Assert.That(report.CleanupFailures.IsEmpty).IsTrue();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(operation.Ledger.RetainedEncodedBytes).IsEqualTo(before);
        await Assert.That(operation.Ledger.RetainedEntries).IsEqualTo(entries);
    }

    private static async Task RefuseAsync(NativeLocalChildLifetimeSeeded state,
        NativeLocalChildLifetimeOperation operation, DecodedGrainRequest verified,
        RequestCqrsHostedRuntimeFixture actual)
    {
        var before = operation.Ledger.RetainedEncodedBytes;
        var entries = operation.Ledger.RetainedEntries;
        var runtime = actual.Services.GetRequiredService<SiloStorageIdentityProvider>().Read();
        await Assert.That(runtime.SiloAddress).IsEqualTo(actual.Services.GetRequiredService<ILocalSiloDetails>()
            .SiloAddress.ToParsableString());
        await NativeLocalChildLifetimeAssertions.RefusedAsync(() =>
            operation.Owner.AcquireVerifiedProducer(verified, state.PhysicalOwner, runtime));
        await Assert.That(operation.Ledger.RetainedEncodedBytes).IsEqualTo(before);
        await Assert.That(operation.Ledger.RetainedEntries).IsEqualTo(entries);
    }
}
