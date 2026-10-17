using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class RetirementConservationHeaderImage
{
    internal static void Write(string path, ZoneTreeStore store, ZoneTreeStoreOptions options, int defect)
    {
        var metadata = new ZoneTreeCheckpointMetadata(ZoneTreePersistenceFormat.CheckpointVersion,
            KeyCodec.Version, store.Identity.Incarnation, store.Position, 0)
        {
            ConservationAuthorityDigest = new byte[32]
        };
        metadata = defect switch
        {
            0 => metadata with { ConservationAuthorityDigest = default },
            1 => metadata with { ConservationAuthorityDigest = new byte[31] },
            2 => metadata with { ConservationAuthorityRecords = -1 },
            3 => metadata with { ConservationAuthorityRecords = 1 },
            4 => metadata with { Version = 5 },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ZoneTreeCheckpointFrame.Write(output, options, ZoneTreePersistenceFormat.CheckpointMagic,
            metadata.Position, NativeSerialization.Serialize(metadata), digest);
        var footer = new ZoneTreeCheckpointFooter(0, Convert.ToHexStringLower(digest.GetHashAndReset()));
        ZoneTreeCheckpointFrame.Write(output, options, ZoneTreePersistenceFormat.CheckpointEndMagic,
            metadata.Position, NativeSerialization.Serialize(footer), null);
        output.Flush(true);
    }
}
