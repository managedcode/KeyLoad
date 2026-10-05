using System.Net;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsAuthorityFaultAssertions
{
    internal const string InitialJson = RequestCqrsRf3Protocol.DocumentJson;
    internal const string HeldJson = RequestCqrsRf3Protocol.ChangedDocumentJson;
    internal const string AdministratorJson = "{\"authority\":\"fresh-admin-write\",\"value\":3}";

    internal static async Task VerifyHeldMarkerAsync(RequestCqrsProbeMarkerRecord marker, Guid armId,
        Guid commandId, IReadOnlyList<ReplicaSiloDiscovery> discovery)
    {
        await Assert.That(marker.ArmId).IsEqualTo(armId);
        await Assert.That(marker.CommandId).IsEqualTo(commandId);
        await Assert.That(marker.RequestId).IsNotEqualTo(Guid.Empty);
        await Assert.That(marker.Phase).IsEqualTo(RequestCqrsProbePhase.AuthorizationReload);
        await Assert.That(marker.Outcome).IsEqualTo(RequestCqrsProbeOutcome.Observed);
        var voter = discovery.SingleOrDefault(item => item.VoterId == marker.Voter);
        await Assert.That(voter).IsNotNull();
        await Assert.That(voter!.SiloAddress).IsEqualTo(marker.SiloAddress);
    }

    internal static async Task VerifyRevocationAckAsync(PrincipalRecord actual, PrincipalRecord expected)
    {
        await Assert.That(actual.Id).IsEqualTo(expected.Id);
        await Assert.That(actual.Revoked).IsTrue();
        await Assert.That(actual.PolicyEpoch).IsEqualTo(expected.PolicyEpoch);
    }

    internal static async Task VerifySdkUnauthorizedAsync(Result<CommitReceipt> result)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(ErrorCode.Unauthenticated.ToString());
    }

    internal static async Task VerifyMcpCredentialRejectedAsync(McpOfficialClient mcp, string secret,
        EntityRef reference, CancellationToken cancellationToken)
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference), cancellationToken));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error.Message.Contains(secret, StringComparison.Ordinal)).IsFalse();
    }

    internal static async Task<Guid?> VerifyMcpUnauthorizedAsync(RequestCqrsFaultMcpObservation observation)
    {
        await Assert.That((observation.ToolResult is not null) ^ (observation.TransportFailure is not null)).IsTrue();
        if (observation.ToolResult is not { } result)
        { throw new InvalidOperationException("The actual MCP operation did not return a typed unauthorized result."); }
        return await McpCallerAssertions.ErrorAsync(result, ErrorCode.Unauthenticated, dispatched: true)
            .ConfigureAwait(false);
    }

    internal static async Task VerifyDocumentAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsAuthorityFaultIdentity identity, string json, long revision, CancellationToken cancellationToken)
    {
        var reference = new EntityRef(identity.Partition, RequestCqrsRf3Protocol.AdminCollection,
            RequestCqrsRf3Protocol.DocumentId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.GetAsync(reference,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await administrator.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        await Assert.That(sdk?.Reference).IsEqualTo(reference);
        await Assert.That(mcp.Value?.Reference).IsEqualTo(reference);
        await Assert.That(sdk?.Revision).IsEqualTo(revision);
        await Assert.That(mcp.Value?.Revision).IsEqualTo(revision);
        await Assert.That(sdk?.Json).IsEqualTo(json);
        await Assert.That(mcp.Value?.Json).IsEqualTo(json);
    }

    internal static async Task VerifyOutboxUnchangedAsync(KeyLoadClient administrator, PartitionRef partition,
        OutboxHead expected, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.OutboxStatusAsync(partition,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(actual.Head).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task VerifyReceiptAsync(CommitReceipt actual, CommitReceipt expected)
    {
        await Assert.That(actual.CommandId).IsEqualTo(expected.CommandId);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
