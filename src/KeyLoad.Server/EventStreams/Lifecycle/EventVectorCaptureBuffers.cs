using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorCaptureBuffers
{
    internal static void Clear(EventVectorCoverageCapture? capture)
        => EventVectorCoverageCaptureBuffers.Clear(capture);
}
