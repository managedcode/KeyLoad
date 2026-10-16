using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Preserves the real native MAC boundary for proofless and altered-carrier refusals.</summary>
internal static class ResourceLifetimeGrantTransportRefusals
{
    private const int LastByteOffset = 1;
    private const byte TamperMask = 1;

    internal static async Task LegacyTransportAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        await ResourceLifetimeGrantRefusals.RejectAsync(state, async () =>
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var mac = state.TargetAdmission.CreateControlMac();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var body = NativeSerialization.Serialize(ResourceLifetimeGrantRefusals.FreshRequest(state));
                    await state.TargetAdmission.VerifyAsync(body, mac.Sign(body, reply: false), token);
                    throw new InvalidOperationException("The old native channel accepted a proofless controlled effect.");
                }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw new InvalidOperationException("The proofless original transport produced no refusal.");
        }, ErrorCode.Unauthenticated);
    }

    internal static async Task TransportAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        await ResourceLifetimeGrantRefusals.RejectAsync(state, async () =>
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var mac = state.TargetAdmission.CreateControlMac();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var bytes = NativeSerialization.Serialize(PartitionControlLifetimeTransportV2.Create(
                        state.Request, state.Proof));
                    var signature = mac.SignControlledLifetime(bytes);
                    bytes[^LastByteOffset] ^= TamperMask;
                    await state.TargetAdmission.VerifyControlledLifetimeAsync(bytes, signature, token);
                    throw new InvalidOperationException("An altered original signed carrier was accepted.");
                }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw new InvalidOperationException("The original altered carrier produced no refusal.");
        }, ErrorCode.Unauthenticated);
    }
}
