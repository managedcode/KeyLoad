using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.DatabaseComposition;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ComposedProjectionLineageClusterPolicy
{
    internal const string ScoreUse = "score.use";
    internal const string ScoreRead = "score.read";
    private const long SchemaVersionStep = 1;

    internal static async Task ConfigureScoreAsync(RequestCqrsClusterCallers admin, QueueReferenceSelectState state, CancellationToken token)
    {
        var catalog = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
            new(state.Partition.TenantId, state.Partition.DatabaseId), token));
        var current = catalog.Items.Single(item => item.Name == QueueReferenceSelectProtocol.Queue);
        var resource = new ResourceDefinition(QueueReferenceSelectProtocol.Queue, ResourceKind.WorkQueue,
            state.Partition.TransactionDomainId)
        {
            SchemaVersion = checked(current.SchemaVersion + SchemaVersionStep),
            FieldPolicies = [new("/to", "reference", RawReadGrant: QueueReferenceSelectProtocol.RefRead,
            RawUseGrant: QueueReferenceSelectProtocol.RefUse), new("/score", "predicate",
            RawReadGrant: ScoreRead, RawUseGrant: ScoreUse)]
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, resource)
            {
                ExpectedSchemaVersion = current.SchemaVersion
            }, token));
        await SqlClusterProtocol.EqualAsync(resource, persisted);
        var after = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
            new(state.Partition.TenantId, state.Partition.DatabaseId), token));
        await Assert.That(after.Items.Single(item => item.Name == resource.Name).SchemaVersion).IsEqualTo(resource.SchemaVersion);
    }

    internal static async Task SetAsync(RequestCqrsClusterCallers admin, QueueReferenceSelectState state,
        long epoch, bool referenceUse, bool scoreUse, bool reads, CancellationToken token, bool scoreRead = true)
    {
        var fields = new List<string>();
        if (scoreRead)
        {
            fields.Add(ScoreRead);
        }
        if (referenceUse)
        {
            fields.Add(QueueReferenceSelectProtocol.RefUse);
        }
        if (scoreUse)
        {
            fields.Add(ScoreUse);
        }
        if (reads)
        {
            fields.Add(QueueReferenceSelectProtocol.RefRead);
            fields.Add(QueueReferenceSelectProtocol.NameRead);
        }
        var principal = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            RestrictRows = true,
            OwnerId = state.Identity.Principal.Id,
            Grants = [new(state.Partition.DatabaseId, QueueReferenceSelectProtocol.Queue, Capability.Query | Capability.QueueInspect),
                new(state.Partition.DatabaseId, QueueReferenceSelectProtocol.EmptyQueue, Capability.Query | Capability.QueueInspect),
                new(state.Partition.DatabaseId, QueueReferenceSelectProtocol.Target, Capability.Query | Capability.DocumentsRead)],
            FieldGrants = [.. fields]
        };
        await SqlClusterProtocol.EqualAsync(principal,
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
    }
}
