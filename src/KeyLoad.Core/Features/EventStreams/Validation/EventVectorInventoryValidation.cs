using System.Collections.Immutable;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorInventoryValidation
{
    private const int Version = 1;
    private const int FirstOrdinal = 0;
    private const int NextOrdinal = 1;
    private const long FirstRevision = 1;
    private const string InvalidInventory = "The native event vector typed inventory is inconsistent.";

    internal static void Require(EventVectorMap header, ImmutableArray<EventVectorEncodedRow> rows,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(admission);
        if (header.Version != Version || header.MapId == Guid.Empty || header.ControlPartition is null
            || string.IsNullOrWhiteSpace(header.PrincipalId) || header.ControlIncarnation == Guid.Empty
            || header.OriginalOpenCommandId == Guid.Empty || header.Revision < FirstRevision
            || header.CoverageGeneration < FirstRevision || header.PageCount < FirstOrdinal
            || header.CleanupGeneration < EventVectorSourcePhaseIds.InitialCleanupGeneration
            || !Enum.IsDefined(header.State) || !Enum.IsDefined(header.StartPolicy)
            || header.Released != (header.State == EventVectorMapState.Released)
            || header.SourceManifestDigest.Length != SHA256.HashSizeInBytes
            || header.OriginalSourceManifestDigest.Length != SHA256.HashSizeInBytes
            || header.OriginalOptionsDigest.Length != SHA256.HashSizeInBytes)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidInventory); }
        _ = EventVectorRowAccounting.PayloadBytes(rows);
        var maximumCoverage = EventVectorInventoryCoverageBound.Read(header, rows, admission);
        var pages = new HashSet<int>();
        var phases = new Dictionary<Guid, EventVectorSourcePhase>();
        var proofs = new Dictionary<long, EventVectorCoverageProof>();
        foreach (var row in rows)
        {
            admission.RequireEncodedBytes(row.EncodedBytes);
            RequireRow(header, row, pages, phases, proofs, admission, maximumCoverage);
        }
        if (pages.Count != header.PageCount)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidInventory); }
        for (var ordinal = FirstOrdinal; ordinal < header.PageCount; ordinal = checked(ordinal + NextOrdinal))
        {
            if (!pages.Contains(ordinal))
            { throw Errors.Fail(ErrorCode.Corruption, InvalidInventory); }
        }
        EventVectorInventoryPhaseValidation.RequirePointers(header, phases);
        EventVectorCoverageProofInventory.Require(header, proofs, maximumCoverage);
    }

    private static void RequireRow(EventVectorMap header, EventVectorEncodedRow row, HashSet<int> pages,
        Dictionary<Guid, EventVectorSourcePhase> phases, Dictionary<long, EventVectorCoverageProof> proofs,
        EventVectorAdmissionPolicy admission, long maximumCoverage)
    {
        if (row.Key.Span.StartsWith(EventVectorKeys.Pages(header.ControlPartition, header.MapId)))
        {
            var page = NativeSerialization.Deserialize<EventVectorPage>(row.Value.Span);
            if (page.Version != Version || page.MapId != header.MapId || page.PageOrdinal < FirstOrdinal
                || !pages.Add(page.PageOrdinal) || !row.Key.Span.SequenceEqual(
                    EventVectorKeys.Page(header.ControlPartition, header.MapId, page.PageOrdinal)))
            { throw Errors.Fail(ErrorCode.Corruption, InvalidInventory); }
            _ = EventVectorEntryEncoding.Decode(page.Entries, page.Checksum, admission);
            return;
        }
        if (row.Key.Span.StartsWith(EventVectorKeys.Phases(header.ControlPartition, header.MapId)))
        {
            var phase = EventVectorInventoryPhaseValidation.Read(header, row, admission, maximumCoverage);
            if (!phases.TryAdd(phase.PhaseCommandId, phase))
            { throw Errors.Fail(ErrorCode.Corruption, InvalidInventory); }
            return;
        }
        if (row.Key.Span.StartsWith(EventVectorKeys.Proofs(header.ControlPartition, header.MapId)))
        {
            EventVectorCoverageProofInventory.Add(header, row, proofs, admission, maximumCoverage);
            return;
        }
        if (row.Key.Span.SequenceEqual(EventVectorKeys.CleanupCall(header.ControlPartition, header.MapId)))
        {
            _ = EventVectorCleanupValidation.Read(header, row, admission);
            return;
        }
        if (row.Key.Span.SequenceEqual(EventVectorKeys.ParentCall(header.ControlPartition, header.MapId)))
        {
            _ = EventVectorParentValidation.Read(header, row, admission);
            return;
        }
        if (!row.Key.Span.SequenceEqual(EventVectorKeys.Offer(header.ControlPartition, header.MapId)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidInventory); }
        var offer = NativeSerialization.Deserialize<EventVectorOffer>(row.Value.Span);
        if (offer.Version != Version || offer.MapId != header.MapId || offer.MapRevision != header.Revision
            || offer.OriginalReadCommandId == Guid.Empty || offer.ExpiresAt <= offer.IssuedAt)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidInventory); }
        _ = EventVectorEntryEncoding.Decode(offer.Entries, offer.PageDigest, admission);
    }
}
