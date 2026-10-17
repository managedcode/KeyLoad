using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorCapturedExchange(EventVectorCoveragePacketOwner originalPacket,
    EventVectorCoverageCapture actualCapture) : IDisposable
{
    private EventVectorCoveragePacketOwner? packet = originalPacket;
    private EventVectorCoverageCapture? capture = actualCapture;

    internal EventVectorCoveragePacket OriginalPacket => packet?.OriginalPacket
        ?? throw new ObjectDisposedException(nameof(EventVectorCapturedExchange));

    internal EventVectorCoverageCapture ActualCapture => capture
        ?? throw new ObjectDisposedException(nameof(EventVectorCapturedExchange));

    public void Dispose()
    {
        EventVectorCaptureBuffers.Clear(capture);
        capture = null;
        packet?.Dispose();
        packet = null;
    }
}
