using System.Collections.Immutable;

namespace KeyLoad.Core;

/// <summary>Requires actual effective owner occurrences without equating independent physical cuts.</summary>
internal static class ClusterBackupPartitionVectorValidation
{
    private const long NoRecords = 0;
    private const int Sha256HexCharacters = 64;
    private const string Inconsistent = "The captured partition vector crosses different movement states or omits its effective owner data cut.";

    internal static void Require(ImmutableArray<ClusterBackupOwnerCut> cuts)
    {
        var canonical = new Dictionary<PartitionRef, AtomicPartitionPlacementResolution>();
        var owned = new Dictionary<PartitionRef, AtomicPartitionPlacementResolution>();
        foreach (var cut in cuts)
        {
            var local = new HashSet<PartitionRef>();
            foreach (var partition in cut.Partitions)
            {
                if (partition?.Roster?.Partition is not { } identity || partition.Placement is not { } placement
                    || placement.VoterIds.IsDefault || !local.Add(identity)
                    || partition.CanonicalRecordCount < NoRecords || partition.CanonicalDigest is null
                    || partition.CanonicalDigest.Length != Sha256HexCharacters
                    || !partition.CanonicalDigest.All(IsHex))
                { throw Errors.Fail(ErrorCode.Corruption, Inconsistent); }
                if (canonical.TryGetValue(identity, out var original) && !SameOwner(original, placement))
                { throw Errors.Fail(ErrorCode.RecoveryRequired, Inconsistent); }
                canonical.TryAdd(identity, placement);
                if (placement.PhysicalShardId == cut.Owner.PhysicalShardId && !owned.TryAdd(identity, placement))
                { throw Errors.Fail(ErrorCode.Corruption, Inconsistent); }
            }
        }
        foreach (var partition in canonical)
        {
            if (!owned.TryGetValue(partition.Key, out var actual) || !SameOwner(partition.Value, actual))
            { throw Errors.Fail(ErrorCode.RecoveryRequired, Inconsistent); }
        }
    }

    private static bool SameOwner(AtomicPartitionPlacementResolution left, AtomicPartitionPlacementResolution right) =>
        left.PhysicalShardId == right.PhysicalShardId && left.Incarnation == right.Incarnation
        && left.PlacementEpoch == right.PlacementEpoch
        && left.VoterIds.SequenceEqual(right.VoterIds, StringComparer.Ordinal);

    private const char FirstDigit = '0';
    private const char LastDigit = '9';
    private const char FirstHexLetter = 'A';
    private const char LastHexLetter = 'F';
    private static bool IsHex(char value) => value is >= FirstDigit and <= LastDigit or >= FirstHexLetter and <= LastHexLetter;
}
