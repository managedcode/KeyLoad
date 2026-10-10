using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferWireFaultSend
{
    internal static async Task ExecuteAsync(PartitionMovementLateNativeOwners owners, RemoteTransferColdSeed seed,
        RemoteTransferOriginalRequestGate gate, RemoteTransferHeldWire held, CancellationToken token)
    {
        var original = RemoteTransferWireNativeCut.Read(owners, seed, token);
        var failures = new List<Exception>();
        try
        {
            using var connection = PartitionMovementLateNativeHttp.Create(new(owners.Settings.Origin(held.Receiver)));
            await ServerFailureObserver.ObserveAsync(() => SendAsync(connection.Client, owners, seed, gate, held, original, token), failures);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task SendAsync(HttpClient client, PartitionMovementLateNativeOwners owners,
        RemoteTransferColdSeed seed, RemoteTransferOriginalRequestGate gate, RemoteTransferHeldWire held,
        RemoteTransferWireNativeCut original, CancellationToken token)
    {
        foreach (var bytes in RemoteTransferWireFaultShapes.Enumerate(held))
        {
            await Assert.That(bytes.AsSpan().SequenceEqual(held.Bytes)).IsFalse();
            var observed = gate.ExpectFault(bytes);
            using var request = new HttpRequestMessage(HttpMethod.Post, RemoteDocumentProtocol.Path);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new(RemoteDocumentProtocol.ContentType);
            request.Headers.Add(RemoteDocumentProtocol.SignatureHeader, held.Signature);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            await Assert.That(response.IsSuccessStatusCode).IsFalse();
            await Assert.That(observed.IsCompleted).IsTrue();
            await Assert.That(await observed).IsEqualTo(ErrorCode.Unauthenticated);
            await original.RequireAsync(RemoteTransferWireNativeCut.Read(owners, seed, token));
        }
    }
}
