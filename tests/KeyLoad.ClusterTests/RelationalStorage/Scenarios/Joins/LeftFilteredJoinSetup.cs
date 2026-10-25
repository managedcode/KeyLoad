using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.ClusterTests.ResourceExecution;

namespace KeyLoad.ClusterTests.RelationalStorage;

internal static class LeftFilteredJoinSetup
{
    private const string OrderIdFieldKey = "order_id";
    private const string IdFieldKey = "id";
    private const long InitialScopedPolicyEpoch = 2;
    internal static async Task<LeftFilteredJoinState> RunAsync(ClusterFixture fixture, KeyLoadClient sdk,
        CancellationToken token)
    {
        var partition = new PartitionRef("left-filter-" + Guid.NewGuid().ToString("N"),
            "database", "domain", Guid.NewGuid().ToString("N"));
        var left = new ResourceDefinition(LeftFilteredJoinProtocol.Left, ResourceKind.Collection,
            partition.TransactionDomainId)
        {
            RelationalSchema = new(OrderIdFieldKey, [new(OrderIdFieldKey, RelationalColumnType.Text),
                new("customer_id", RelationalColumnType.Text), new("total", RelationalColumnType.WholeNumber)]),
            FieldPolicies = [new("/total", "filter", RawReadGrant: LeftFilteredJoinProtocol.FilterReadGrant, RawUseGrant: LeftFilteredJoinProtocol.UseGrant)]
        };
        var right = new ResourceDefinition(LeftFilteredJoinProtocol.Right, ResourceKind.Collection,
            partition.TransactionDomainId)
        {
            RelationalSchema = new(IdFieldKey, [new(IdFieldKey, RelationalColumnType.Text), new("name", RelationalColumnType.Text)]),
            FieldPolicies = [new("/name", "private", RawReadGrant: LeftFilteredJoinProtocol.NameReadGrant)]
        };
        var leftLifetime = await ConfigureAsync(sdk, partition, left, token);
        var rightLifetime = await ConfigureAsync(sdk, partition, right, token);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(LeftFilteredJoinProtocol.Left, "a", LeftFilteredJoinProtocol.LeftA),
                new PutDocument(LeftFilteredJoinProtocol.Left, "b", LeftFilteredJoinProtocol.LeftB),
                new PutDocument(LeftFilteredJoinProtocol.Right, "c1", LeftFilteredJoinProtocol.RightA),
                new PutDocument(LeftFilteredJoinProtocol.Right, "c2", LeftFilteredJoinProtocol.RightB)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition,
            LeftFilteredJoinProtocol.Left, Capability.Query | Capability.DocumentsRead, token);
        var state = new LeftFilteredJoinState(partition, command, receipt, identity, leftLifetime, rightLifetime);
        await RepairAsync(sdk, state, InitialScopedPolicyEpoch, allowPredicate: false, readName: false, token);
        return state;
    }

    private static async Task<EntityRefLifetime> ConfigureAsync(KeyLoadClient sdk, PartitionRef partition,
        ResourceDefinition definition, CancellationToken token)
    {
        var commandId = Guid.NewGuid();
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        await SqlClusterProtocol.SdkAsync<ResourceDefinition>(sdk, SqlClusterProtocol.Call(partition,
            McpCallerTools.ResourcesConfigure, request, commandId), token);
        return ResourceCreationLifetime.Create(commandId, request);
    }

    internal static async Task RepairAsync(KeyLoadClient sdk, LeftFilteredJoinState state,
        long epoch, bool allowPredicate, bool readName, CancellationToken token)
    {
        var grants = new List<string> { LeftFilteredJoinProtocol.FilterReadGrant };
        if (allowPredicate)
        {
            grants.Add(LeftFilteredJoinProtocol.UseGrant);
        }
        if (readName)
        {
            grants.Add(LeftFilteredJoinProtocol.NameReadGrant);
        }
        var principal = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            Grants = [new(state.Partition.DatabaseId, LeftFilteredJoinProtocol.Left, Capability.Query | Capability.DocumentsRead),
                new(state.Partition.DatabaseId, LeftFilteredJoinProtocol.Right, Capability.Query | Capability.DocumentsRead)],
            FieldGrants = [.. grants]
        };
        await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
    }
}
