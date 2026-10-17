using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsTextResource
{
    internal static async Task<ResourceLifetimeV1> ConfigureAsync(RequestCqrsHostedDatabase database,
        CancellationToken token)
    {
        var partition = database.Partition;
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
            new(NativeTextBilingualAudit.Collection, ResourceKind.Collection, partition.TransactionDomainId));
        var id = Guid.NewGuid();
        var configured = database.Submit(OperationKind.ConfigureResource, request,
            RequestCqrsHostedProfile.Root, id, null, token).Get<ResourceDefinition>();
        await Assert.That(JsonDefaults.Serialize(configured).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(request.Definition))).IsTrue();
        var original = database.ReadRetainedOperation(id, token);
        await Assert.That(original.Id).IsEqualTo(id);
        await Assert.That(original.Kind).IsEqualTo(OperationKind.ConfigureResource);
        await Assert.That(original.PrincipalId).IsEqualTo(RequestCqrsHostedProfile.Root);
        await Assert.That(JsonDefaults.Serialize(NativeCommandPayload.Read<ConfigureResourceRequest>(original)).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
        return ResourceLifetimeIdentity.Create(original, request.TenantId, request.DatabaseId, request.Definition);
    }
}
