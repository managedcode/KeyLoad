using static KeyLoad.RecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.EventStreams;

internal static class SharedDomainBindingFlow
{
    internal static async Task RunAsync(DatabaseEngine database, SharedDomainState state, CancellationToken token)
    {
        foreach (var scope in state.Scopes)
        {
            foreach (var resource in scope.Resources)
            {
                var key = KeySpace.Resource(scope.Partition.TenantId, scope.Partition.DatabaseId, resource.Name);
                var original = SharedDomainImage.Required(database.Store, key);
                var replacement = resource with
                {
                    TransactionDomainId = resource.TransactionDomainId == FirstDomain ? SecondDomain : FirstDomain,
                    SchemaVersion = SecondRevision
                };
                var request = new ConfigureResourceRequest(scope.Partition.TenantId, scope.Partition.DatabaseId, replacement)
                {
                    ExpectedSchemaVersion = FirstRevision
                };
                var operation = SharedDomainOperations.Operation(database, OperationKind.ConfigureResource, Guid.NewGuid(), request, Root);
                var result = SharedDomainOperations.Apply(database, operation, token);
                await SharedDomainValueAssertions.FailureAsync(result, ErrorCode.UnsupportedCapability, BindingDetail);
                await SharedDomainImage.BytesAsync(database.Store, key, original);
                var image = SharedDomainImage.Capture(database.Store);
                var position = database.Store.Position;
                await SharedDomainValueAssertions.EqualAsync(SharedDomainOperations.Apply(database, operation, token), result);
                await SharedDomainImage.SameAsync(database.Store, image, position);
            }
        }
        await SharedDomainModelAssertions.AllAsync(database, state, token);
    }
}
