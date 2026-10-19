namespace KeyLoad.Server.Features.Search;

internal static class AnnPublicReadAdmission
{
    internal static AnnReadReservation Reserve(List<AnnGenerationSlot> slots,
        AnnExecutionOptions options, AnnReadReservations readers, long desiredBytes, long maintenanceBytes)
    {
        var receipt = AnnRootLease.ReceiptReservation(options);
        AnnSlotRetention.RequireResident(slots, options, checked(receipt + readers.Bytes + maintenanceBytes));
        var available = checked(options.MaximumResidentBytes - receipt - readers.Bytes - maintenanceBytes
            - slots.Sum(item => item.Index.RetainedBytesUpperBound));
        return readers.Reserve(Math.Min(desiredBytes, available));
    }

    internal static AnnManifest Describe(List<AnnGenerationSlot> slots, AnnOwnedKey key)
        => slots.SingleOrDefault(item => !item.Retired
            && AnnSlotRetention.SameKey(item.Manifest, key))?.Manifest
            ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
}
