using KeyLoad.Core.ResourceExecution;
using KeyLoad.UnitTests.Search;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class RequestCqrsTextResource
{
    internal static async Task<ResourceLifetime> ConfigureAsync(RequestCqrsHostedDatabase database,
        CancellationToken token)
    {
        var partition = database.Partition;
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
            new(TextBilingualAudit.Collection, ResourceKind.Collection, partition.TransactionDomainId));
        var id = Guid.NewGuid();
        var original = database.Database.CreateCommand(OperationKind.ConfigureResource, id,
            RequestCqrsHostedProfile.Root, database.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request));
        var configured = (await database.Coordinator.SubmitVerifiedAsync(original, token).ConfigureAwait(false))
            .Get<ResourceDefinition>();
        await Assert.That(JsonDefaults.Serialize(configured).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(request.Definition))).IsTrue();
        await Assert.That(original.CommandId).IsEqualTo(id);
        await Assert.That(original.Value!.Kind).IsEqualTo(OperationKind.ConfigureResource);
        await Assert.That(original.UserId!).IsEqualTo(RequestCqrsHostedProfile.Root);
        await Assert.That(JsonDefaults.Serialize(OrleansSerialization.Deserialize<ConfigureResourceRequest>((original).Value!.Input.Span)).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
        return ResourceLifetimeIdentity.Create(original, request.TenantId, request.DatabaseId, request.Definition);
    }
}
