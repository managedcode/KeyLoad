using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Reads the issuer's bounded typed floor inside an already owned, optionally budgeted native view.</summary>
internal static class ResourceRetirementConservationRead
{
    private const int PresenceProbeRecords = 1;
    private const int NoRecords = 0;
    private const string Missing = "The native retirement conservation floor is absent while issued records remain.";
    private const string Oversized = "The native retirement conservation record exceeds its admitted byte limit.";

    internal static ResourceRetirementConservationFloorV1? ReadFloor(IKeyValueView view, int maximumRecordBytes,
        StorageReadObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecordBytes);
        ResourceRetirementConservationFloorV1? floor = null;
        var found = view.ReadValue(ResourceRetirementConservationKeys.Floor(), value =>
        {
            if (value.Length > maximumRecordBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, Oversized);
            }
            floor = NativeSerialization.Deserialize<ResourceRetirementConservationFloorV1>(value);
            ResourceRetirementConservation.Validate(floor);
        }, observer);
        if (found)
        {
            return floor;
        }
        if (HasRecord(view, ResourceRetirementConservationKeys.StepPrefix(), observer)
            || HasRecord(view, ResourceRetirementConservationKeys.ReleasePrefix(), observer)
            || HasRecord(view, ResourceRetirementAuthorityKeys.ReleasePrefix(), observer)
            || HasRecord(view, ResourceRetirementConservationOriginKeys.Prefix(), observer))
        {
            throw Errors.Fail(ErrorCode.Corruption, Missing);
        }
        return null;
    }

    private static bool HasRecord(IKeyValueView view, byte[] prefix, StorageReadObserver? observer)
        => view.VisitRange(prefix, PresenceProbeRecords, static (_, _) => false, observer: observer).Records != NoRecords;
}
