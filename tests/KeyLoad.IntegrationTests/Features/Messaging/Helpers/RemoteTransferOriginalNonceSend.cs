using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferOriginalNonceSend
{
    internal static async Task<RemoteTransferPostAwaitNativeCut> ExecuteAsync(PartitionMovementLateNativeOwners owners,
        RemoteTransferColdSeed seed, RemoteTransferOriginalRequestGate gate, RemoteTransferHeldWire held,
        CommandRequest accept, CancellationToken token)
    {
        var failures = new List<Exception>();
        RemoteTransferPostAwaitNativeCut? committed = null;
        try
        {
            using var connection = PartitionMovementLateNativeHttp.Create(new(owners.Settings.Origin(held.Receiver)));
            await ServerFailureObserver.ObserveAsync(async () => committed = await ExecuteCoreAsync(connection.Client,
                owners, seed, gate, held, accept, token), failures);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
        return committed ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
    }

    private static async Task<RemoteTransferPostAwaitNativeCut> ExecuteCoreAsync(HttpClient client,
        PartitionMovementLateNativeOwners owners, RemoteTransferColdSeed seed, RemoteTransferOriginalRequestGate gate,
        RemoteTransferHeldWire held, CommandRequest accept, CancellationToken token)
    {
        var before = RemoteTransferWireNativeCut.Read(owners, seed, token);
        using var first = await SendAsync(client, held, token);
        await Assert.That(first.IsSuccessStatusCode).IsTrue();
        var native = RemoteTransferPostAwaitNativeCut.Read(owners, seed, accept);
        await RemoteTransferOriginalNonceReply.RequireAsync(first, held, owners, native, token);
        var committed = RemoteTransferWireNativeCut.Read(owners, seed, token);
        await new RemoteTransferWireNativeCut(before.Owners[..PartitionMovementLateNativeSettings.GroupSize])
            .RequireAsync(new(committed.Owners[..PartitionMovementLateNativeSettings.GroupSize]));
        var observed = gate.ExpectFault(held.Bytes);
        using var duplicate = await SendAsync(client, held, token);
        await Assert.That(duplicate.IsSuccessStatusCode).IsFalse();
        await Assert.That(observed.IsCompleted).IsTrue();
        await Assert.That(await observed).IsEqualTo(ErrorCode.Unauthenticated);
        await committed.RequireAsync(RemoteTransferWireNativeCut.Read(owners, seed, token));
        await native.RequireAsync(RemoteTransferPostAwaitNativeCut.Read(owners, seed, accept));
        return native;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, RemoteTransferHeldWire held,
        CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RemoteDocumentProtocol.Path);
        request.Content = new ByteArrayContent(held.Bytes);
        request.Content.Headers.ContentType = new(RemoteDocumentProtocol.ContentType);
        request.Headers.Add(RemoteDocumentProtocol.SignatureHeader, held.Signature);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }
}
