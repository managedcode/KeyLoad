using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsNativeReplayOriginalBytesTests
{
    [Test]
    public async Task RoundTripRetainsActualIssuedIntentAndOriginalNativeBytes()
    {
        using var fixture = new RequestCqrsNativeReplayFrameCase();
        var original = fixture.Create();
        var restored = RequestCqrsNativeReplayInventory.ReadWitness(RequestCqrsNativeReplayInventory.WriteWitness(original));
        await Assert.That(restored.OriginalNativeRequest.SequenceEqual(original.OriginalNativeRequest)).IsTrue();
        var request = RequestCqrsNativeReplayInventory.Original(restored);
        await Assert.That(fixture.Mac.VerifyNativeRequest(request)).IsTrue();
        await Assert.That(fixture.Mac.Verify(request.Payload.Span, request.Signature, false)).IsTrue();
        await Assert.That(RequestCqrsNativeReplayInventory.Reply(restored).Error).IsEqualTo(ErrorCode.Unauthenticated);
    }

    [Test]
    public async Task RefusesMissingOriginalAndCrossOriginalNonceCommandOrInnerSignature()
    {
        using var fixture = new RequestCqrsNativeReplayFrameCase();
        var value = fixture.Create();
        var json = JsonNode.Parse(RequestCqrsNativeReplayInventory.WriteWitness(value))!.AsObject();
        json.Remove(nameof(value.OriginalNativeRequest));
        _ = Assert.ThrowsExactly<JsonException>(() => RequestCqrsNativeReplayInventory.ReadWitness(JsonSerializer.SerializeToUtf8Bytes(json)));
        var duplicate = RequestCqrsProbeCodecInput.InsertFirstProperty(RequestCqrsNativeReplayInventory.WriteWitness(value), "Ordinal", "1");
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeReplayInventory.ReadWitness(duplicate));
        var original = RequestCqrsNativeReplayInventory.Original(value);
        var actual = RequestCqrsNativeReplayInventory.Request(value);
        var wrongNonce = value with
        {
            OriginalNativeRequest = NativeSerialization.Serialize(original with
            {
                Nonce = actual.Nonce
            })
        };
        var wrongCommand = value with
        {
            CommandId = Guid.NewGuid()
        };
        var wrongMac = value with
        {
            OriginalNativeRequest = NativeSerialization.Serialize(original with
            {
                Signature = original.Signature + "!"
            })
        };
        foreach (var invalid in new[] { wrongNonce, wrongCommand, wrongMac })
        {
            _ = Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeReplayInventory.WriteWitness(invalid));
        }
        await Assert.That(RequestCqrsNativeReplayInventory.ReadWitness(RequestCqrsNativeReplayInventory.WriteWitness(value)).RequestId).IsEqualTo(value.RequestId);
    }

    [Test]
    public async Task AllFixedVariationsKeepOriginalInnerSignatureAndUseFreshValidOuterMac()
    {
        using var fixture = new RequestCqrsNativeReplayFrameCase();
        foreach (var mode in Enum.GetValues<RemoteModelNativeReplayVariation>().Where(RemoteModelNativeReplayCatalog.IsIndividual))
        {
            var witness = fixture.Create(mode);
            var restored = RequestCqrsNativeReplayInventory.ReadWitness(RequestCqrsNativeReplayInventory.WriteWitness(witness));
            var original = RequestCqrsNativeReplayInventory.Original(restored);
            var actual = RequestCqrsNativeReplayInventory.Request(restored);
            await Assert.That(actual.Signature).IsEqualTo(original.Signature);
            await Assert.That(actual.Nonce == original.Nonce).IsFalse();
            await Assert.That(fixture.Mac.VerifyNativeRequest(actual)).IsTrue();
            await Assert.That(fixture.Mac.Verify(actual.Payload.Span, actual.Signature, false))
                .IsEqualTo(mode == RemoteModelNativeReplayVariation.Original2);
        }
    }
}
