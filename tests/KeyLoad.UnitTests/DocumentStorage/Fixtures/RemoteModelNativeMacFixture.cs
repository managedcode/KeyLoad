using System.Net;
using System.Security.Cryptography;
using KeyLoad.Server;
using KeyLoad.Server.Features.DocumentStorage;
namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class RemoteModelNativeMacFixture : IDisposable
{
    internal byte[] Credential { get; } = RandomNumberGenerator.GetBytes(RemoteDocumentProtocol.SecretBytes);
    internal RemoteDocumentMac Mac
    {
        get;
    }
    internal RemoteModelNativeMacFixture()
    {
        try
        {
            Mac = new(Credential);
        }
        catch (Exception)
        {
            CryptographicOperations.ZeroMemory(Credential);
            throw;
        }
    }

    internal RemoteModelNativeRequest Request()
    {
        ReadOnlyMemory<byte> payload = new byte[] { 0, 1, 127, 128, 255 };
        return new(RemoteModelNativeChannel.DocumentEnvelope, payload, Mac.Sign(payload.Span, false),
            new(Guid.NewGuid(), Guid.NewGuid(), ["control-a", "control-b", "control-c"], 1),
            new(Guid.NewGuid(), Guid.NewGuid(), ["destination-a", "destination-b", "destination-c"], 2),
            "control-a", Address(11111, 31), "destination-b", Address(11112, 47),
            Guid.NewGuid(), TimeProvider.System.GetUtcNow().AddSeconds(1), string.Empty);
    }

    internal RemoteModelNativeReply Reply(RemoteModelNativeRequest request)
    {
        ReadOnlyMemory<byte> payload = new byte[] { 255, 128, 127, 1, 0 };
        return new(payload, Mac.Sign(payload.Span, true), null, null, request.Nonce, string.Empty);
    }

    internal static string Address(int port, int generation)
        => SiloAddress.New(new IPEndPoint(IPAddress.Loopback, port), generation).ToParsableString();

    internal static async Task RunAsync(Func<RemoteModelNativeMacFixture, Task> scenario)
    {
        List<Exception> failures = [];
        try
        {
            using var fixture = new RemoteModelNativeMacFixture();
            await ServerFailureObserver.ObserveAsync(() => scenario(fixture), failures);
        }
        catch (Exception failure) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void Dispose()
    {
        try
        {
            Mac.Dispose();
        }
        finally { CryptographicOperations.ZeroMemory(Credential); }
    }
}
