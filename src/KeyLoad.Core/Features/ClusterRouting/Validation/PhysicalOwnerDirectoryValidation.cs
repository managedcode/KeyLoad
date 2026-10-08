using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PhysicalOwnerDirectoryValidation
{
    private const int MinimumOwnerCount = 2;
    private const int ControlOwnerCount = 1;

    internal static void Validate(PhysicalOwnerDirectoryV1? value)
    {
        if (value is null || value.Version != PhysicalOwnerDirectoryProtocol.Version
            || value.Revision <= PhysicalOwnerDirectoryProtocol.EmptyRevision || value.ControlOwner is null
            || value.Owners.IsDefault || value.Owners.Length is < MinimumOwnerCount or > PhysicalOwnerDirectoryProtocol.MaximumOwners
            || !value.Owners.All(PhysicalOwnerEntryValidation.Valid))
        { throw Errors.Fail(ErrorCode.Corruption, PhysicalOwnerDirectoryProtocol.Malformed); }
        var controls = value.Owners.Where(entry => PhysicalOwnerEntryValidation.SameOwner(entry.Owner, value.ControlOwner)).ToArray();
        if (controls.Length != ControlOwnerCount || !Unique(value.Owners))
        { throw Errors.Fail(ErrorCode.Corruption, PhysicalOwnerDirectoryProtocol.Malformed); }
    }

    internal static bool Unique(IEnumerable<RegisteredPhysicalOwnerV1> entries)
    {
        var all = entries.ToArray();
        return all.Select(entry => entry.Owner.PhysicalShardId).Distinct().Count() == all.Length
            && all.Select(entry => entry.Owner.Incarnation).Distinct().Count() == all.Length
            && UniqueStrings(all.SelectMany(entry => entry.Owner.VoterIds))
            && UniqueStrings(all.SelectMany(entry => entry.Endpoints));
    }

    private static bool UniqueStrings(IEnumerable<string> values)
    {
        var all = values.ToArray();
        return all.Distinct(StringComparer.Ordinal).Count() == all.Length;
    }

    internal static void ValidateRegistration(RegisterPhysicalOwnerV1? request)
    {
        if (request is null || request.Version != PhysicalOwnerDirectoryProtocol.Version
            || request.ExpectedRevision < PhysicalOwnerDirectoryProtocol.EmptyRevision
            || !PhysicalOwnerEntryValidation.Valid(request.Control)
            || !PhysicalOwnerEntryValidation.Valid(request.Destination)
            || !Unique([request.Control, request.Destination]))
        { throw Errors.Fail(ErrorCode.Validation, PhysicalOwnerDirectoryProtocol.InvalidRegistration); }
    }
}
