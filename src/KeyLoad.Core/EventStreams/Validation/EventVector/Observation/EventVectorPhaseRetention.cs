using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorPhaseRetention
{
    private const string InvalidRetention = "The original native event vector source phase must remain retained.";

    internal static void Require(EventVectorMap header, ImmutableArray<EventVectorEncodedRow> original,
        ImmutableArray<EventVectorEncodedRow> proposed, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(admission);
        var prefix = EventVectorKeys.Phases(header.ControlPartition, header.MapId);
        var retained = proposed.Where(row => row.Key.Span.StartsWith(prefix))
            .ToDictionary(row => Convert.ToHexString(row.Key.Span), StringComparer.Ordinal);
        foreach (var row in original)
        {
            if (!row.Key.Span.StartsWith(prefix))
            {
                continue;
            }
            if (!retained.TryGetValue(Convert.ToHexString(row.Key.Span), out var replacement))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidRetention);
            }
            var before = EventVectorInventoryPhaseValidation.Read(header, row, admission,
                EventVectorInventoryCoverageBound.Read(header, proposed, admission));
            if (before.Disposition == EventVectorSourcePhaseDisposition.Observed)
            {
                if (!row.Value.Span.SequenceEqual(replacement.Value.Span))
                {
                    throw Errors.Fail(ErrorCode.Conflict, InvalidRetention);
                }
                continue;
            }
            var after = EventVectorInventoryPhaseValidation.Read(header, replacement, admission,
                EventVectorInventoryCoverageBound.Read(header, proposed, admission));
            if (!before.OriginalNativeBody.Span.SequenceEqual(after.OriginalNativeBody.Span)
                || before.ControlPolicyEpoch != after.ControlPolicyEpoch
                || before.OriginalPosition != after.OriginalPosition
                || before.ControlIncarnation != after.ControlIncarnation)
            {
                throw Errors.Fail(ErrorCode.Conflict, InvalidRetention);
            }
        }
    }
}
