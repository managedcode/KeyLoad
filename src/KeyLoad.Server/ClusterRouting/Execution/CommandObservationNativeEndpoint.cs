using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Authenticates one closed native action and publishes inside its existing original observation frame.</summary>
internal sealed class CommandObservationNativeEndpoint(PartitionHost partition, IOptions<NodeOptions> options,
    SiloStorageIdentityProvider local, PartitionMovementGrainServiceClient movement, NativeCommandObservationTarget target)
{
    internal async Task<CommandObservationNativeReply> ExecuteAsync(CommandObservationNativeRequest request,
        CancellationToken token)
    {
        using var mac = CommandObservationNativeMac.Create(options.Value, partition);
        CommandObservationNativeValidation.Require(request, partition, options.Value, local.Read(), mac);
        if (request.Action == CommandObservationNativeAction.Abort)
        {
            await AdmitPeerAsync(token).ConfigureAwait(false);
            await target.AbortAsync(request.OpaqueToken, token).ConfigureAwait(false);
            var reply = new CommandObservationNativeReply(request.Nonce, request.Signature, new(), string.Empty);
            CommandObservationNativeMac.RequireSize(reply, partition);
            return reply with
            {
                Signature = CommandObservationNativeMac.Sign(mac, reply)
            };
        }
        return request.Action == CommandObservationNativeAction.Execute
            ? await target.ExecuteAsync(request.OpaqueToken, AdmitPeerAsync,
                (reply, work) => CommandObservationNativeMac.SignReply(mac, request, reply, work, partition),
                Clear, token).ConfigureAwait(false)
            : await target.ReadAsync(request.OpaqueToken, AdmitPeerAsync,
                (reply, work) => CommandObservationNativeMac.SignReply(mac, request, reply, work, partition),
                Clear, token).ConfigureAwait(false);

        async Task AdmitPeerAsync(CancellationToken active)
        {
            await movement.RequireCallerAsync(PhysicalOwnerConfiguredTuples.Local(options.Value, partition.Configuration),
                request.Caller.VoterId, request.Caller.SiloAddress, active).ConfigureAwait(false);
            CommandObservationNativeValidation.Require(request, partition, options.Value, local.Read(), mac);
        }
    }

    private static void Clear(CommandObservationNativeReply reply)
    {
        if (MemoryMarshal.TryGetArray(reply.Result.Payload, out var owned) && owned.Array is not null)
        {
            CryptographicOperations.ZeroMemory(owned.Array.AsSpan(owned.Offset, owned.Count));
        }
    }
}
