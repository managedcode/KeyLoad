namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class WireSupportedScalarDepthTests
{
    [Test]
    public async Task AcIsPerf006JaggedArrayTypeAdmissionPreservesThe264DepthFence()
    {
        var edge = WireSupportedScalarFixtures.ArrayOf(typeof(int), 263);
        await Assert.That(LeafDepth(edge)).IsEqualTo(SerializationLimits.WireDepth);
        WireSupported.Require(edge);

        var beyond = WireSupportedScalarFixtures.ArrayOf(typeof(int), 264);
        await Assert.That(LeafDepth(beyond)).IsEqualTo(checked(SerializationLimits.WireDepth + 1));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => WireSupported.Require(beyond));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(PayloadVersion.InvalidPayload);
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
