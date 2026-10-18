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
        var original = database.Database.CreateNativeOperation(OperationKind.ConfigureResource, id,
            RequestCqrsHostedProfile.Root, database.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var configured = (await database.Coordinator.SubmitVerifiedAsync(original, token).ConfigureAwait(false))
            .Get<ResourceDefinition>();
        await Assert.That(JsonDefaults.Serialize(configured).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(request.Definition))).IsTrue();
        await Assert.That(original.Id).IsEqualTo(id);
        await Assert.That(original.Kind).IsEqualTo(OperationKind.ConfigureResource);
        await Assert.That(original.PrincipalId).IsEqualTo(RequestCqrsHostedProfile.Root);
        await Assert.That(JsonDefaults.Serialize(NativeCommandPayload.Read<ConfigureResourceRequest>(original)).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
        return ResourceLifetimeIdentity.Create(original, request.TenantId, request.DatabaseId, request.Definition);
    }
}
