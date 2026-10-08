using System.Security.Cryptography;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Returns only the actual configured MAC/DNS admission output for a native source capability.</summary>
internal static class ControlledPartitionMovementPeerVerification
{
    internal static async Task<PartitionMovementTransportRequest> VerifyAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        PartitionMovementTransportRequest original, CancellationToken cancellationToken)
    {
        if (ControlledPartitionMovementProcessScope.Current is { } process)
        { return await ControlledPartitionMovementProcessScope.VerifyAsync(source, runtime, original, cancellationToken); }
        PartitionMovementTransportRequest? verified = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var key = Convert.FromBase64String(runtime.Node.Value.PeerSecret);
            try
            {
                using var mac = new PartitionMovementMac(key, source.Database.Limits.MaxBatchBytes);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var bytes = NativeSerialization.Serialize(original);
                    verified = await admission.VerifyAsync(bytes, mac.Sign(bytes, reply: false), cancellationToken);
                }, failures);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return verified ?? throw new InvalidOperationException("The original native capability was not admitted.");
    }
}
