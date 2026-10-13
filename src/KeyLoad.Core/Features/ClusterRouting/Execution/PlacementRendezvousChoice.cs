using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting;

internal static class PlacementRendezvousChoice
{
    private const int EqualScore = 0;
    private const int ScoreOffset = 0;
    private const string OwnerIdentityFormat = "N";
    private const int ScoreBytes = sizeof(ulong);

    internal static Guid Choose(TenantPlacementPolicyV1 policy, IEnumerable<Guid> eligible)
    {
        Guid? best = null;
        ulong bestScore = default;
        var bestWeight = default(int);
        foreach (var owner in eligible.OrderBy(item => item.ToString(OwnerIdentityFormat), StringComparer.Ordinal))
        {
            var configured = policy.OwnerWeights.SingleOrDefault(item => item.PhysicalShardId == owner)
                ?? throw Errors.Fail(ErrorCode.Validation, Contracts.TenantPlacementPolicyProtocol.Invalid);
            var bytes = SHA256.HashData(KeyCodec.Encode(Contracts.TenantPlacementPolicyProtocol.Version,
                policy.TenantId, policy.DatabaseId, owner));
            var score = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(ScoreOffset, ScoreBytes));
            if (best is null || PlacementRendezvousScore.Compare(score, configured.Weight, bestScore, bestWeight) > EqualScore)
            { best = owner; bestScore = score; bestWeight = configured.Weight; }
        }
        return best ?? throw Errors.Fail(ErrorCode.ResourceExhausted, Contracts.TenantPlacementPolicyProtocol.ScopeMismatch);
    }
}
