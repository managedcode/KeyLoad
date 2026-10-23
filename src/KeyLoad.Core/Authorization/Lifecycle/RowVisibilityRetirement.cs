namespace KeyLoad.Core.Features.Authorization;

internal static class RowVisibilityRetirement
{
    internal static void Sweep(ref RowVisibilityEntry? head, ref bool closed, ICacheMemoryReservation ownerCharge,
        ref bool ownerReleased, ref Exception? ownerReleaseFailure)
    {
        var failures = new List<Exception>();
        RowVisibilityEntry? previous = null;
        for (var entry = head; entry is not null;)
        {
            var next = entry.Next;
            var before = failures.Count;
            if (entry.Retired && entry.Pins == RowVisibilityMemoryModel.MetadataOnly)
            {
                RowVisibilityFailureLedger.Capture(entry.Dispose, failures);
            }
            if (entry.Retired && entry.Pins == RowVisibilityMemoryModel.MetadataOnly && failures.Count == before)
            {
                if (previous is null)
                {
                    head = next;
                }
                else
                {
                    previous.Next = next;
                }
            }
            else
            {
                previous = entry;
            }
            entry = next;
        }
        if (closed && head is null && !ownerReleased)
        {
            if (ownerReleaseFailure is not null)
            {
                failures.Add(ownerReleaseFailure);
            }
            else
            {
                RowVisibilityFailureLedger.Capture(ownerCharge.Dispose, failures);
                ownerReleaseFailure = failures.Count == RowVisibilityMemoryModel.MetadataOnly ? null : failures[^RowVisibilityMemoryModel.SingleEntry];
            }
            ownerReleased = failures.Count == RowVisibilityMemoryModel.MetadataOnly;
        }
        RowVisibilityFailureLedger.Throw(failures);
    }

    internal static void ReleasePin(RowVisibilityEntry entry, ICacheMemoryReservation charge,
        ref bool chargeReleased, ref bool pinReleased, ref Exception? releaseFailure)
    {
        if (!chargeReleased)
        {
            if (releaseFailure is not null)
            {
                RowVisibilityFailureLedger.Throw([releaseFailure]);
            }
            try
            {
                charge.Dispose();
            }
            catch (Exception error)
            {
                releaseFailure = error;
                throw;
            }
            chargeReleased = true;
        }
        if (!pinReleased)
        {
            entry.Pins--;
            pinReleased = true;
        }
    }
}
