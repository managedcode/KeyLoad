using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Validates the independent target identity and paths before an original restore slot is admitted.</summary>
internal static class ZoneTreeRestoreSlotTargetAdmission
{
    internal static void Require(StoreIdentity original, ClusterRestoreSlotContext context,
        ReadOnlyMemory<byte> signingKey, ref string destination, ref string stage)
    {
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        stage = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stage));
        if (stage == destination || context.TargetNodeId == Guid.Empty || context.TargetIncarnation == original.Incarnation
            || signingKey.Length != SigningKeyBytes || context.TargetNodeId == original.NodeId
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(signingKey.Span)),
                context.TargetSignerFingerprint, StringComparison.Ordinal)
            || CryptographicOperations.FixedTimeEquals(original.SigningKey.Span, signingKey.Span))
        {
            throw Errors.Fail(ErrorCode.Validation, ZoneTreeRestoreSlotDriver.Invalid);
        }
    }
}
