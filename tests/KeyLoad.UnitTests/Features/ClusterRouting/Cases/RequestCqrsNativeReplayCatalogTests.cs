using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsNativeReplayCatalogTests
{
    [Test]
    public async Task SixActualGeneratedVariationsShareOriginalBytesAndRequireExactPlanOrder()
    {
        using var fixture = new RequestCqrsNativeReplayFrameCase();
        var original = fixture.Create();
        var witnesses = Capture(fixture, original);
        var armBytes = RequestCqrsProbeCodecInput.Arm(RequestCqrsProbePhase.NativeModelReplayBeforeDispatch,
            commandId: original.CommandId, armId: original.ArmId, principalId: RequestCqrsNativeReplayFrameCase.Principal);
        armBytes = RequestCqrsProbeCodecInput.Replace(armBytes, RequestCqrsProbeCodecInput.SessionId, original.SessionId);
        var arm = UnitRequestProbeOptions.Json.ReadArm(armBytes);
        var plan = new RequestCqrsNativeReplayPlan(original.Version, RequestCqrsNativeReplayInventory.PlanKind,
            original.SessionId, original.ArmId, original.CommandId, RequestCqrsNativeReplayFrameCase.Principal,
            RemoteModelNativeReplayVariation.AllWireFaults6);
        var marker = new RequestCqrsProbeMarkerRecord(original.Version, RequestCqrsProbeProtocol.MarkerKind,
            original.SessionId, original.ArmId, original.RequestId, original.CommandId,
            RequestCqrsProbePhase.NativeModelReplayBeforeDispatch, RequestCqrsProbeOutcome.Observed,
            original.Voter, original.SiloAddress);
        RequestCqrsNativeReplayInventory.RequireInventory([plan], witnesses, [new(arm, armBytes)], [marker], _ => null);
        await Assert.That(witnesses.Length).IsEqualTo(6);
        await Assert.That(witnesses.Select(value => RequestCqrsNativeReplayInventory.Request(value).Nonce).Distinct().Count()).IsEqualTo(6);
        foreach (var witness in witnesses)
        {
            await Assert.That(RequestCqrsNativeReplayInventory.IsWitnessName(RequestCqrsNativeReplayInventory.WitnessName(
                witness.ArmId, witness.RequestId, witness.Ordinal))).IsTrue();
            var request = RequestCqrsNativeReplayInventory.Request(witness);
            await Assert.That(fixture.Mac.VerifyNativeRequest(request)).IsTrue();
            await Assert.That(fixture.Mac.Verify(request.Payload.Span, request.Signature, reply: false)).IsFalse();
            await Assert.That(witness.OriginalNativeRequest.SequenceEqual(original.OriginalNativeRequest)).IsTrue();
        }
        var wrongOrder = witnesses.ToArray();
        wrongOrder[0] = wrongOrder[0] with { Ordinal = 2 };
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeReplayInventory.RequireInventory([plan], wrongOrder,
            [new(arm, armBytes)], [marker], _ => null));
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeReplayInventory.RequireInventory([plan], witnesses[..5],
            [new(arm, armBytes)], [marker], _ => null));
    }

    [Test]
    public async Task OriginalAndSingleModesRetainCapsAndPlanModeIsNeverWitnessVariation()
    {
        await Assert.That(RemoteModelNativeReplayCatalog.Count(RemoteModelNativeReplayVariation.Original2)).IsEqualTo(2);
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => RemoteModelNativeReplayCatalog.At(RemoteModelNativeReplayVariation.Original2, 3));
        foreach (var mode in Enum.GetValues<RemoteModelNativeReplayVariation>().Where(RemoteModelNativeReplayCatalog.IsIndividual))
        {
            if (mode == RemoteModelNativeReplayVariation.Original2) { continue; }
            await Assert.That(RemoteModelNativeReplayCatalog.Count(mode)).IsEqualTo(1);
            _ = Assert.ThrowsExactly<InvalidOperationException>(() => RemoteModelNativeReplayCatalog.At(mode, 2));
        }
        using var fixture = new RequestCqrsNativeReplayFrameCase();
        var invalid = fixture.Create() with { ReplayMode = RemoteModelNativeReplayVariation.AllWireFaults6 };
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeReplayInventory.WriteWitness(invalid));
        await Assert.That(RequestCqrsNativeReplayInventory.IsWitnessName(RequestCqrsNativeReplayInventory.WitnessName(
            invalid.ArmId, invalid.RequestId, 7))).IsFalse();
    }

    private static RequestCqrsNativeReplayWitness[] Capture(RequestCqrsNativeReplayFrameCase fixture, RequestCqrsNativeReplayWitness basis)
    {
        var original = RequestCqrsNativeReplayInventory.Original(basis);
        var result = new RequestCqrsNativeReplayWitness[6];
        for (var ordinal = 1; ordinal <= result.Length; ordinal++)
        {
            var mode = RemoteModelNativeReplayCatalog.At(RemoteModelNativeReplayVariation.AllWireFaults6, ordinal);
            var request = original with { Payload = RemoteModelNativeReplayPayload.Create(original, mode), Nonce = Guid.NewGuid(), SenderSignature = string.Empty };
            request = request with { SenderSignature = fixture.Mac.SignNativeRequest(request) };
            var reply = RequestCqrsNativeReplayInventory.Reply(basis) with { Nonce = request.Nonce, SenderSignature = string.Empty };
            reply = reply with { SenderSignature = fixture.Mac.SignNativeReply(reply) };
            result[ordinal - 1] = RequestCqrsNativeReplayInventory.ReadWitness(RequestCqrsNativeReplayInventory.WriteWitness(basis with
            { ReplayMode = mode, Ordinal = ordinal, NativeRequest = NativeSerialization.Serialize(request), NativeReply = NativeSerialization.Serialize(reply) }));
        }
        return result;
    }
}
