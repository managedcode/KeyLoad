using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorAbandonmentComplete
{
    private const int FirstOrdinal = 0;
    private const long NextPass = 1;
    private const string Invalid = "The cleanup pass has unsettled original source ownership.";

    internal static EventVectorMap Execute(IAtomicTransaction transaction, EventVectorControlPhase control,
        IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var request = control.OriginalRequest;
        var inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition, request.MapId,
            options, cancellationToken);
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        if (control.Action != EventVectorControlAction.CompleteRelease || map.State != EventVectorMapState.Releasing
            || map.PendingPhaseId is not null || map.CleanupGeneration != control.ExpectedCleanupGeneration)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var originalEntries = EventVectorControlPages.Entries(inventory, admission);
        var suppliedEntries = EventVectorEntryEncoding.Decode(control.OriginalEncodedEntries,
            control.OriginalEntryChecksum, admission);
        if (!originalEntries.SequenceEqual(suppliedEntries))
        {
            throw Errors.Fail(ErrorCode.Conflict, Invalid);
        }
        var frontier = NativeSerialization.Deserialize<ImmutableArray<EventVectorCleanupPin>>(
            control.OriginalCleanupFrontierBytes.Span);
        if (frontier.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        admission.RequireEntryCount(frontier.Length);
        var releases = ReadPass(inventory, map, admission);
        if (releases.Count != frontier.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var retained = false;
        for (var ordinal = FirstOrdinal; ordinal < frontier.Length; ordinal++)
        {
            if (!releases.TryGetValue(ordinal, out var release))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
            }
            EventVectorCleanupSourceBinding.Require(control, release, admission);
            var mutation = EventVectorSourceMutationValidation.ReadObserved(release, admission);
            var result = release.SourceOriginalResult!.Get<EventVectorSourceReleaseResult>();
            EventVectorReleaseResultValidation.Require(release, mutation, result, admission);
            retained |= result.Disposition == EventVectorReleaseDisposition.Retained;
        }
        var changed = map with
        {
            State = retained ? EventVectorMapState.Releasing : EventVectorMapState.Released,
            Released = !retained,
            CleanupGeneration = retained ? checked(map.CleanupGeneration + NextPass)
                : map.CleanupGeneration
        };
        if (retained)
        {
            return EventVectorMapWriter.Write(transaction, changed, inventory.PayloadRows, options, cancellationToken);
        }
        EventVectorControlRelease.DeleteSettled(transaction, inventory, changed, admission, cancellationToken);
        return changed;
    }

    private static Dictionary<int, EventVectorSourcePhase> ReadPass(EventVectorOwnedInventory inventory,
        EventVectorMap map, EventVectorAdmissionPolicy admission)
    {
        var prefix = EventVectorKeys.Phases(map.ControlPartition, map.MapId);
        var coverage = EventVectorInventoryCoverageBound.Read(map, inventory.PayloadRows, admission);
        var releases = new Dictionary<int, EventVectorSourcePhase>();
        foreach (var row in inventory.PayloadRows)
        {
            if (!row.Key.Span.StartsWith(prefix))
            {
                continue;
            }
            var phase = EventVectorInventoryPhaseValidation.Read(map, row, admission, coverage);
            if (phase.Role != EventVectorSourcePhaseRole.Release || phase.MapRevision != map.Revision
                || phase.CleanupGeneration != map.CleanupGeneration)
            {
                continue;
            }
            if (phase.Disposition != EventVectorSourcePhaseDisposition.Observed
                || phase.SourceOriginalResult is not { Error: null }
                || !releases.TryAdd(phase.SourceOrdinal, phase))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
            }
        }
        return releases;
    }
}
