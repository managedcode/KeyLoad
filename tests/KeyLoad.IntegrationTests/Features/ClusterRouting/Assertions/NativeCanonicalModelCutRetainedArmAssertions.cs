using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeCanonicalModelCutRetainedArmAssertions
{
    private const string Invalid = "The authentic original diagnostic arm history differs from its settled owner state.";

    internal static void RequireSettledOriginal(RequestCqrsProbeFixture controls, Guid armId,
        ReadOnlyMemory<byte> originalBytes)
    {
        var state = controls.ArmFor(armId);
        if (!state.Settled || state.ProcessOwnerJoined || state.Retired || state.ArmId != armId
            || state.Action != RequestCqrsProbeAction.Hold || state.RequestId is null
            || !state.Bytes.AsSpan().SequenceEqual(originalBytes.Span))
        {
            throw new InvalidDataException(Invalid);
        }
        var actual = NativeCanonicalModelCutOriginalArm.Read(controls, armId);
        if (!actual.AsSpan().SequenceEqual(originalBytes.Span) || !state.Settled
            || state.ProcessOwnerJoined || state.Retired)
        {
            throw new InvalidDataException(Invalid);
        }
    }

    internal static void RequireKilledOwnerRefusal(RequestCqrsProbeFixture controls, Guid armId,
        ReadOnlyMemory<byte> originalBytes)
    {
        var state = controls.ArmFor(armId);
        if (!state.ProcessOwnerJoined || state.Retired || state.ArmId != armId
            || !state.Bytes.AsSpan().SequenceEqual(originalBytes.Span))
        {
            throw new InvalidDataException(Invalid);
        }
        _ = Assert.ThrowsExactly<IOException>(() => NativeCanonicalModelCutOriginalArm.Read(controls, armId));
        if (!state.ProcessOwnerJoined || state.Retired || !state.Bytes.AsSpan().SequenceEqual(originalBytes.Span))
        {
            throw new InvalidDataException(Invalid);
        }
    }
}
