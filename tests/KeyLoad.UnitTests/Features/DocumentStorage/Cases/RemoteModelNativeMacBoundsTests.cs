using KeyLoad.Server.Features.DocumentStorage;
namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class RemoteModelNativeMacBoundsTests
{
    [Test]
    public async Task OriginalEightMiBPayloadRemainsSignableWithBoundedNativeMetadata()
    {
        await RemoteModelNativeMacFixture.RunAsync(async fixture =>
        {
            var request = fixture.Request() with
            {
                Payload = new byte[RemoteDocumentProtocol.MaximumBodyBytes]
            };
            await Assert.That(NativeSerialization.Measure(request) <= RemoteModelNativeProtocol.MaximumEnvelopeBytes).IsTrue();
            request = request with
            {
                SenderSignature = fixture.Mac.SignNativeRequest(request)
            };
            await Assert.That(fixture.Mac.VerifyNativeRequest(request)).IsTrue();
            var reply = fixture.Reply(request) with
            {
                Payload = request.Payload
            };
            reply = reply with
            {
                SenderSignature = fixture.Mac.SignNativeReply(reply)
            };
            await Assert.That(fixture.Mac.VerifyNativeReply(reply)).IsTrue();
        });
    }

    [Test]
    public async Task OversizedWholeNativeEnvelopeRefusesInsteadOfTruncatingOriginalBytes()
    {
        await RemoteModelNativeMacFixture.RunAsync(async fixture =>
        {
            var request = fixture.Request() with
            {
                Payload = new byte[RemoteModelNativeProtocol.MaximumEnvelopeBytes]
            };
            await Assert.That(NativeSerialization.Measure(request) > RemoteModelNativeProtocol.MaximumEnvelopeBytes).IsTrue();
            var requestError = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Mac.SignNativeRequest(request));
            await Assert.That(requestError.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            var reply = fixture.Reply(request) with
            {
                Payload = request.Payload
            };
            var replyError = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Mac.SignNativeReply(reply));
            await Assert.That(replyError.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(fixture.Mac.VerifyNativeRequest(request with
            {
                SenderSignature = string.Empty
            })).IsFalse();
            await Assert.That(fixture.Mac.VerifyNativeReply(reply with
            {
                SenderSignature = new string('z', 64)
            })).IsFalse();
        });
    }
}
