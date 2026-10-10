using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionRf3PublicOperations
{
    internal static async Task<ConnectionRf3Reply<CommitReceipt>> CommitAsync(ConnectionRf3Caller caller,
        CommandRequest command, bool useMcp, CancellationToken cancellationToken)
    {
        if (useMcp)
        {
            var actual = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.CallAsync(
                McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            return new(actual.Value, actual.RequestId);
        }
        return new(await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false), null);
    }

    internal static async Task<ConnectionRf3Reply<DocumentResult?>> ReadAsync(ConnectionRf3Caller caller,
        RequestCqrsPhaseFaultIdentity identity, bool useMcp, CancellationToken cancellationToken)
    {
        var reference = Reference(identity);
        if (useMcp)
        {
            var actual = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await caller.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);
            return new(actual.Value, actual.RequestId);
        }
        return new(await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.GetAsync(reference,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false), null);
    }

    internal static EntityRef Reference(RequestCqrsPhaseFaultIdentity identity)
        => new(identity.Partition, RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId);

    internal static async Task ReceiptAsync(ConnectionRf3Reply<CommitReceipt> reply, CommandRequest command,
        ConnectionProbeWitness witness)
    {
        await Assert.That(reply.Value.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(reply.Value.Mutations.Length).IsEqualTo(ConnectionRf3Protocol.OneConnection);
        await Assert.That(reply.Value.Mutations[0].Resource).IsEqualTo(RequestCqrsRf3Protocol.AdminCollection);
        await Assert.That(reply.Value.Mutations[0].Id).IsEqualTo(RequestCqrsRf3Protocol.DocumentId);
        await Assert.That(reply.Value.Mutations[0].Revision).IsEqualTo(ConnectionRf3Protocol.UpdatedRevision);
        if (reply.RequestId is { } actual)
        { await Assert.That(actual).IsEqualTo(witness.RequestId); }
    }

    internal static async Task DocumentAsync(ConnectionRf3Reply<DocumentResult?> reply,
        RequestCqrsPhaseFaultIdentity identity, ConnectionProbeWitness witness, string json, long revision)
    {
        await Assert.That(reply.Value?.Reference).IsEqualTo(Reference(identity));
        await Assert.That(reply.Value?.Json).IsEqualTo(json);
        await Assert.That(reply.Value?.Revision).IsEqualTo(revision);
        await Assert.That(reply.Value?.Redacted).IsFalse();
        if (reply.RequestId is { } actual)
        { await Assert.That(actual).IsEqualTo(witness.RequestId); }
    }

    internal static async Task ErrorAsync(ConnectionRf3Caller caller, CommandRequest command, bool useMcp,
        ErrorCode expected, CancellationToken cancellationToken)
    {
        if (useMcp)
        {
            await McpCallerAssertions.ErrorAsync(await caller.CallAsync(McpCallerTools.DocumentsCommit,
                command, cancellationToken).ConfigureAwait(false), expected, dispatched: true).ConfigureAwait(false);
            return;
        }
        var result = await caller.Sdk.CommitAsync(command, cancellationToken).ConfigureAwait(false);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
    }
}
