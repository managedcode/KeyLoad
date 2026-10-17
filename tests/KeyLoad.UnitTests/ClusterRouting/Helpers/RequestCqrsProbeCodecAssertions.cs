using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsProbeCodecAssertions
{
    internal static async Task InvalidOwnerAsync(byte[] bytes)
    {
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => UnitRequestProbeOptions.Json.ReadOwner(bytes));
        await Assert.That(failure.Message).IsEqualTo(RequestCqrsProbeProtocol.InvalidRecord);
    }

    internal static async Task InvalidArmAsync(byte[] bytes)
    {
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => UnitRequestProbeOptions.Json.ReadArm(bytes));
        await Assert.That(failure.Message).IsEqualTo(RequestCqrsProbeProtocol.InvalidRecord);
    }

    internal static async Task InvalidReleaseAsync(byte[] bytes)
    {
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => UnitRequestProbeOptions.Json.ReadRelease(bytes));
        await Assert.That(failure.Message).IsEqualTo(RequestCqrsProbeProtocol.InvalidRecord);
    }

    internal static async Task InvalidMarkerAsync(byte[] bytes)
    {
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => UnitRequestProbeOptions.Json.ReadMarker(bytes));
        await Assert.That(failure.Message).IsEqualTo(RequestCqrsProbeProtocol.InvalidRecord);
    }
}
