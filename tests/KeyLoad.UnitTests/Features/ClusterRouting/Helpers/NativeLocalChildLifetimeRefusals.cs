using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

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

    private static async Task RefuseAsync(NativeLocalChildLifetimeSeeded state,
        NativeLocalChildLifetimeOperation operation, DecodedGrainRequest verified,
        RequestCqrsCohortNativeNode actual)
    {
        var before = operation.Ledger.RetainedEncodedBytes;
        var entries = operation.Ledger.RetainedEntries;
        var runtime = actual.Discovery.Read();
        await Assert.That(runtime.TransportReady).IsTrue();
        await Assert.That(runtime.SiloAddress).IsEqualTo(actual.Services.GetRequiredService<ILocalSiloDetails>()
            .SiloAddress.ToParsableString());
        await NativeLocalChildLifetimeAssertions.RefusedAsync(() =>
            operation.Owner.AcquireVerifiedProducer(verified, state.PhysicalOwner, runtime));
        await Assert.That(operation.Ledger.RetainedEncodedBytes).IsEqualTo(before);
        await Assert.That(operation.Ledger.RetainedEntries).IsEqualTo(entries);
    }
}
