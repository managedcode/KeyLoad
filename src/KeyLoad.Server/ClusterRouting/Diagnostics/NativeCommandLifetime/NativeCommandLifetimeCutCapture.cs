using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class NativeCommandLifetimeCutCapture
{
    internal static NativeCommandLifetimeCutWitness Read(DatabaseEngine database, ReplicaSiloDiscovery owner,
        NativeCommandLifetimeCutRequest request, IOptions<DatabaseLimits> limits, CancellationToken token)
    {
        NativeCommandLifetimeCutCodec.Validate(request);
        token.ThrowIfCancellationRequested();
        return database.Store.Read(view =>
        {
            if (owner.Incarnation != database.Store.Identity.Incarnation)
            {
                throw NativeCanonicalModelCutCodec.Invalid();
            }
            using var reader = new NativeCommandLifetimeCutReader(limits, token);
            var models = reader.ReadModels(view, request.Partition);
            var logical = reader.Outcome(view, request.PrincipalId, request.CommandId, request.Partition);
            var technical = reader.Outcome(view, request.TechnicalPrincipalId, request.EffectCommandId, request.Partition);
            token.ThrowIfCancellationRequested();
            return new NativeCommandLifetimeCutWitness(request, owner, database.Store.Position, models.Rows, models.Bytes, models.Digest, logical, technical);
        });
    }
}
