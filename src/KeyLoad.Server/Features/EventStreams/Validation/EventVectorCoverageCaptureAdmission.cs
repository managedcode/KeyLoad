using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorCoverageCaptureAdmission
{
    private const int Version = 1;
    private const long InitialCut = 0;

    internal static EventVectorCoverageCapture Read(EventVectorCoveragePacket originalPacket,
        EventVectorCoverageCall originalRequest, RegisteredPhysicalOwnerV1 actualDestination,
        EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(originalPacket);
        ArgumentNullException.ThrowIfNull(originalRequest);
        ArgumentNullException.ThrowIfNull(actualDestination);
        admission.RequireEncodedBytes(originalPacket.OriginalCaptureBytes.Length);
        work.ChargeBytes(originalPacket.OriginalCaptureBytes.Length);
        var capture = NativeSerialization.Deserialize<EventVectorCoverageCapture>(
            originalPacket.OriginalCaptureBytes.Span);
        var admitted = false;
        try
        {
            if (capture.Version != Version || capture.MapId != originalRequest.OriginalRequest.MapId
                || capture.ControlPartition != originalRequest.OriginalRequest.ControlPartition
                || capture.Scope != originalRequest.OriginalRequest.Scope
                || capture.PrincipalId != originalRequest.PrincipalId
                || capture.GroupOrdinal != originalRequest.GroupOrdinal || capture.SourceOwner is null
                || !PhysicalOwnerEntryValidation.SameOwner(capture.SourceOwner, actualDestination.Owner)
                || capture.NodeId == Guid.Empty || capture.ReadGeneration < InitialCut
                || capture.StoreCutPosition < InitialCut || capture.AppliedCutPosition < InitialCut
                || capture.HasMore || capture.PhysicalCatalog is null
                || capture.RegisteredOwnerRows is null || capture.PlacementRows is null
                || capture.AtomicPartitionRosterRows is null || capture.SourceHeadRows is null
                || capture.RosterOriginRows is null)
            { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Invalid); }
            _ = EventVectorEntryEncoding.Decode(capture.OriginalEncodedEntries, capture.EntryChecksum, admission);
            work.Check();
            admitted = true;
            return capture;
        }
        finally
        {
            if (!admitted)
            { EventVectorCaptureBuffers.Clear(capture); }
        }
    }
}
