using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorFirstCaptureDirectory
{
    internal static void Require(PhysicalOwnerDirectoryV1 current,
        EventVectorCoverageCapture capture, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        EventVectorCurrentCaptureDirectory.Require(current, capture, admission, work);
    }
}
