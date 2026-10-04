using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeWireSupportedScalarDepthTests
{
    [Test]
    public async Task AcIsPerf006JaggedArrayTypeAdmissionPreservesThe264DepthFence()
    {
        var edge = NativeWireSupportedScalarFixtures.ArrayOf(typeof(int), 263);
        await Assert.That(LeafDepth(edge)).IsEqualTo(NativeSerializationLimits.WireDepth);
        NativeWireSupported.Require(edge);

        var beyond = NativeWireSupportedScalarFixtures.ArrayOf(typeof(int), 264);
        await Assert.That(LeafDepth(beyond)).IsEqualTo(checked(NativeSerializationLimits.WireDepth + 1));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeWireSupported.Require(beyond));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(NativePayloadVersion.InvalidPayload);
    }

    private static int LeafDepth(Type type)
    {
        var depth = 1;
        while (type.IsArray)
        {
            depth++;
            type = type.GetElementType()!;
        }
        return depth;
    }
}
