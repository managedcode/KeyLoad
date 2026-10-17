using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

internal static class EventVectorCurrentCaptureDirectory
{
    private const string Invalid = "The native event vector source proof is invalid.";
    private const int OwnerDirectoryRowCount = 1;
    private const int FirstOrdinal = 0;

    internal static void Require(PhysicalOwnerDirectoryV1 current,
        EventVectorCoverageCapture capture, EventVectorAdmissionPolicy admission, ReadExecutionBudget? originalWork = null)
    {
        if (capture.RegisteredOwnerRows.Length != OwnerDirectoryRowCount)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        }
        var row = capture.RegisteredOwnerRows[FirstOrdinal];
        if (row is null || !row.Key.Span.SequenceEqual(PhysicalOwnerDirectorySerialization.Key()))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        admission.RequireEncodedBytes(row.Value.Length);
        originalWork?.ChargeBytes(row.Value.Length);
        var observed = NativeSerialization.Deserialize<PhysicalOwnerDirectoryV1>(row.Value.Span);
        PhysicalOwnerDirectoryValidation.Validate(observed);
        if (observed.Version != current.Version || observed.Revision != current.Revision
            || !PhysicalOwnerEntryValidation.SameOwner(observed.ControlOwner, current.ControlOwner)
            || observed.Owners.Length != current.Owners.Length)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        }
        for (var ordinal = FirstOrdinal; ordinal < current.Owners.Length; ordinal++)
        {
            var expected = current.Owners[ordinal];
            var actual = observed.Owners[ordinal];
            if (!PhysicalOwnerEntryValidation.SameOwner(actual.Owner, expected.Owner)
                || !actual.Endpoints.SequenceEqual(expected.Endpoints, StringComparer.Ordinal))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
            }
        }
    }
}
