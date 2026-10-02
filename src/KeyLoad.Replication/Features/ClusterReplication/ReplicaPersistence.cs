using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaPersistence
{
    internal const string GuidFormat = "N";
    internal const string SystemKey = "system";
    internal const string AppliedKey = "last-applied";
    internal const string InvalidVote = "A replica cannot lower its term or vote twice in one term.";
    internal const string InvalidCommit = "The replica commit position must be monotonic and within its durable log.";
    internal const string MissingEntry = "The requested replica position is outside the retained log.";
    internal const string ReadLimit = "The replica entry exceeds the append byte budget.";
    internal const string CanonicalAhead = "The canonical applied position has no matching committed replica-log evidence.";
    internal const string InvalidEncoding = "The replica payload encoding is invalid or corrupt.";
    internal const int FileBufferBytes = 65_536;
    internal const int ManifestMaxBytes = 4_096;
    internal const int HashHexLength = 64;

    internal static T Decode<T>(byte[] bytes)
    {
        try
        {
            return JsonDefaults.Deserialize<T>(bytes);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.CorruptLog);
        }
    }

    internal static void ValidateSnapshot(ReplicaSnapshot snapshot, ReplicaConfiguration configuration)
    {
        if (snapshot.TransferId == Guid.Empty || snapshot.Incarnation != configuration.Incarnation
            || snapshot.Index <= 0 || snapshot.Term <= 0 || snapshot.Length <= 0 || snapshot.Length > configuration.MaxSnapshotBytes
            || snapshot.Sha256 is null || snapshot.Sha256.Length != HashHexLength
            || !snapshot.Sha256.All(char.IsAsciiHexDigit)
            || snapshot.FileName != snapshot.TransferId.ToString(GuidFormat) + ReplicaProtocol.SnapshotExtension)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidSnapshot);
        }
    }

    internal static long AppliedPosition(IAtomicStore store) => store.Read(view =>
        view.ReadOwnedValue(KeyCodec.Encode(SystemKey, AppliedKey)) is { } bytes ? Decode<long>(bytes) : 0);

    internal static void VerifyImage(IAtomicStore store, string path, ReplicaSnapshot snapshot)
    {
        using (var image = File.OpenRead(path))
        {
            if (image.Length != snapshot.Length
                || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(image)), snapshot.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot);
            }
        }
        StorageSnapshot cut;
        try
        { cut = store.VerifySnapshot(path); }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot);
        }
        if (cut.Incarnation != snapshot.Incarnation || cut.AppliedPosition != snapshot.Index)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot);
        }
    }
}
