namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnPublicReadAdmission
{
    internal static NativeAnnReadReservation Reserve(List<NativeAnnGenerationSlot> slots,
        NativeAnnExecutionOptions options, NativeAnnReadReservations readers, long desiredBytes, long maintenanceBytes)
    {
        NativeAnnSlotRetention.RequireResident(slots, options, checked(readers.Bytes + maintenanceBytes));
        var available = checked(options.MaximumResidentBytes - readers.Bytes - maintenanceBytes
            - slots.Sum(item => item.Index.RetainedBytesUpperBound));
        return readers.Reserve(Math.Min(desiredBytes, available));
    }

    internal static NativeAnnManifest Describe(List<NativeAnnGenerationSlot> slots, NativeAnnOwnedKey key)
        => slots.SingleOrDefault(item => !item.Retired
            && NativeAnnSlotRetention.SameKey(item.Manifest, key))?.Manifest
            ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory);
}
