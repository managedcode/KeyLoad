using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Server;
using KeyLoad.Server.Features.DocumentStorage;
namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class RemoteDocumentMacLifecycleTests
{
    [Test]
    public async Task OriginalInnerDomainBytesAndOwnedKeyReleaseRemainExact()
    {
        var credential = Enumerable.Range(0, RemoteDocumentProtocol.SecretBytes).Select(value => (byte)value).ToArray();
        var originalCredential = credential.ToArray();
        using var mac = new RemoteDocumentMac(credential);
        var owned = (byte[])typeof(RemoteDocumentMac).GetField("key", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mac)!;
        var body = Encoding.UTF8.GetBytes("original native inner model body");
        const string request = "56564cd2db9fb425cc3bab4556c2f73618b7b61fd5db6611c4f56d045ecaa024";
        const string reply = "42f35f15b571c11772868c5dd18549111d57c0eed1207c26e4e54158f76858b9";
        await Assert.That(mac.Sign(body, false)).IsEqualTo(request);
        await Assert.That(mac.Sign(body, true)).IsEqualTo(reply);
        await Assert.That(mac.Verify(body, request, false)).IsTrue();
        await Assert.That(mac.Verify(body, reply, true)).IsTrue();
        await Assert.That(mac.Verify(body, request, true)).IsFalse();
        await Assert.That(ReferenceEquals(owned, credential)).IsFalse();
        mac.Dispose();
        await Assert.That(owned.All(value => value == 0)).IsTrue();
        await Assert.That(credential.SequenceEqual(originalCredential)).IsTrue();
        mac.Dispose();
        await Assert.That(owned.All(value => value == 0)).IsTrue();
        CryptographicOperations.ZeroMemory(credential);
        CryptographicOperations.ZeroMemory(originalCredential);
    }

    [Test]
    public async Task ClosedOwnerRefusesOriginalAndNativeSigningAndVerification()
    {
        await RemoteModelNativeMacFixture.RunAsync(fixture =>
        {
            var request = fixture.Request();
            var reply = fixture.Reply(request);
            request = request with
            {
                SenderSignature = fixture.Mac.SignNativeRequest(request)
            };
            reply = reply with
            {
                SenderSignature = fixture.Mac.SignNativeReply(reply)
            };
            fixture.Mac.Dispose();
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Mac.Sign(request.Payload.Span, false));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Mac.Verify(request.Payload.Span, request.Signature, false));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Mac.Verify(request.Payload.Span, string.Empty, false));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Mac.SignNativeRequest(request));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Mac.VerifyNativeRequest(request));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Mac.VerifyNativeRequest(request with { SenderSignature = string.Empty }));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Mac.SignNativeReply(reply));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Mac.VerifyNativeReply(reply));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Mac.VerifyNativeReply(reply with { SenderSignature = string.Empty }));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task ConcurrentOriginalAndNativeSigningReturnsOriginalProofOrActualDisposedFailure()
    {
        await RemoteModelNativeMacFixture.RunAsync(async fixture =>
        {
            var request = fixture.Request();
            var reply = fixture.Reply(request);
            var expected = new[] { fixture.Mac.Sign(request.Payload.Span, false), fixture.Mac.Sign(reply.Payload.Span, true),
                fixture.Mac.SignNativeRequest(request), fixture.Mac.SignNativeReply(reply) };
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var outcomes = 0;
            var original = Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
            {
                await start.Task;
                for (var iteration = 0; iteration < 128; iteration++)
                {
                    try
                    {
                        var signature = Sign(fixture.Mac, request, reply, index % expected.Length);
                        await Assert.That(signature).IsEqualTo(expected[index % expected.Length]);
                    }
                    catch (ObjectDisposedException) { }
                    Interlocked.Increment(ref outcomes);
                }
            })).ToArray();
            var close = Task.Run(async () => { await start.Task; fixture.Mac.Dispose(); });
            start.SetResult();
            List<Exception> failures = [];
            foreach (var task in original)
            {
                await ServerFailureObserver.ObserveAsync(() => task, failures);
            }
            await ServerFailureObserver.ObserveAsync(() => close, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            await Assert.That(outcomes).IsEqualTo(32 * 128);
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Mac.SignNativeRequest(request));
        });
    }

    private static string Sign(RemoteDocumentMac mac, RemoteModelNativeRequest request, RemoteModelNativeReply reply, int channel)
        => channel switch
        {
            0 => mac.Sign(request.Payload.Span, false),
            1 => mac.Sign(reply.Payload.Span, true),
            2 => mac.SignNativeRequest(request),
            3 => mac.SignNativeReply(reply),
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
}
