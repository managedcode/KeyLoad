using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Copies a single genuine node-local canonical cut; caller owns admission and current identity.</summary>
internal static class NativeCanonicalModelCutCapture
{
    internal static NativeCanonicalModelCutWitness Capture(DatabaseEngine owner,
        ReplicaSiloDiscovery actualOwner, NativeCanonicalModelCutRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(owner);
        NativeCanonicalModelCutCodec.ValidateRequest(request);
        token.ThrowIfCancellationRequested();
        var result = owner.Store.Read(view =>
        {
            token.ThrowIfCancellationRequested();
            if (actualOwner is null || actualOwner.Incarnation != owner.Store.Identity.Incarnation)
            { throw NativeCanonicalModelCutCodec.Invalid(); }
            var reader = new NativeCanonicalModelCutReader(owner.OperationLimitsOptions, token);
            return reader.Read(view, request, actualOwner, owner.Store.Position);
        });
        token.ThrowIfCancellationRequested();
        NativeCanonicalModelCutCodec.RequireEnvelope(result, token);
        return result;
    }
}
