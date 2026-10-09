using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Core.Features.BackupRestore.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Checks complete current-format canonical scope against the same-view roster, retaining only a digest.</summary>
internal static class ClusterBackupCanonicalCensus
{
    private const string MissingScope = "The current cluster archive contains a canonical scope absent from its roster.";
    private const string CensusExceeded = "The current cluster archive census exceeds the original operation bounds.";

    internal sealed record Scope(long Count, string Digest);
    internal sealed record Census(string Digest, IReadOnlyDictionary<PartitionRef, Scope> Partitions);
    private const long NoRecords = 0;
    private const long RecordStep = 1;
    private const int GlobalHashCount = 1;
    private const int NoFailures = 0;
    private const int PrimaryFailureIndex = 0;

    internal static Census Require(IKeyValueView view, Dictionary<PartitionRef, ClusterBackupRosterCapture.CapturedEntry> entries,
        DatabaseLimits limits, CancellationToken cancellationToken)
    {
        var owners = new List<IncrementalHash>(checked(entries.Count + GlobalHashCount));
        var scoped = new Dictionary<PartitionRef, IncrementalHash>(entries.Count);
        var counts = new Dictionary<PartitionRef, long>(entries.Count);
        Exception? primary = null;
        try
        {
            var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            owners.Add(digest);
            foreach (var partition in entries.Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                owners.Add(hash);
                scoped.Add(partition, hash);
                counts.Add(partition, NoRecords);
            }
            var range = view.VisitRange([], limits.MaxScanRecords, (key, value) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (AtomicPartitionRosterKeyValidation.TryReadCandidate(key, value, hasValue: true, out var partition)
                    && partition is not null && !entries.ContainsKey(partition))
                { throw Errors.Fail(ErrorCode.Corruption, MissingScope); }
                if (partition is not null)
                {
                    Append(scoped[partition], key);
                    Append(scoped[partition], value);
                    counts[partition] = checked(counts[partition] + RecordStep);
                }
                Append(digest, key);
                Append(digest, value);
                return true;
            }, cancellationToken: cancellationToken);
            if (range.HasMore || range.StoppedByVisitor)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, CensusExceeded); }
            cancellationToken.ThrowIfCancellationRequested();
            var result = scoped.ToDictionary(entry => entry.Key,
                entry => new Scope(counts[entry.Key], Convert.ToHexString(entry.Value.GetHashAndReset())));
            return new(Convert.ToHexString(digest.GetHashAndReset()), result);
        }
        catch (Exception error) { primary = error; throw; }
        finally { DisposeScopes(owners, primary); }
    }

    private static void DisposeScopes(IEnumerable<IncrementalHash> hashes, Exception? primary)
    {
        var failures = new List<Exception>();
        foreach (var hash in hashes)
        {
            try
            { DisposeHash(hash); }
            catch (AggregateException cleanup) { failures.AddRange(cleanup.InnerExceptions); }
        }
        if (failures.Count == NoFailures)
        { return; }
        if (primary is not null)
        { failures.Insert(PrimaryFailureIndex, primary); }
        throw new AggregateException(failures);
    }

    // The collection envelope rethrows the SAME original failure, including fatal originals.
    // Its owning loop joins every actual hash before raising the complete ordered failure ledger.
    private static void DisposeHash(IncrementalHash hash)
    {
        try
        { hash.Dispose(); }
        catch (Exception original) { throw new AggregateException(original); }
    }

    private static void Append(IncrementalHash digest, ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(length, bytes.Length);
        digest.AppendData(length);
        digest.AppendData(bytes);
    }
}
