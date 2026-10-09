using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class EmptyReplicaSnapshotPolicyAssertions
{
    private const string Collection = "snapshots";
    private const string Document = "ordered-tail";
    private const string Json = "{\"tail\":true}";
    private const long Revision = 1;

    internal static async Task DeniedThenHealthyAsync(ClusterFixture fixture, string node, string credential,
        PartitionRef partition, CommitToken minimum, CancellationToken token)
    {

        var failures = new List<Exception>();
        McpOfficialClient? deniedMcp = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var deniedSdk = fixture.Client(node, credential);
            deniedMcp = await McpOfficialClient.ConnectAsync(fixture, node, credential, token);
            var reference = new EntityRef(partition, Collection, Document);
            var denied = await deniedSdk.GetAsync(reference, minimum, token);
            await Assert.That(denied.IsSuccess).IsFalse();
            await Assert.That(denied.Value).IsNull();
            await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
            var reply = await deniedMcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, minimum), token);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, credential, Json);
            var admin = fixture.Client(node);
            var healthy = await McpCallerAssertions.SdkSuccessAsync(await admin.GetAsync(reference, minimum, token));
            await EmptyReplicaSnapshotAssertions.ExactAsync(healthy, new DocumentResult(reference, Revision, Json, false, []));
        }, failures);
        if (deniedMcp is not null)
        { await ServerFailureObserver.ObserveAsync(() => deniedMcp.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
