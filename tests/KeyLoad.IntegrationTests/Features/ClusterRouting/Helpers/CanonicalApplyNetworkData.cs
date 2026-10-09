using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class CanonicalApplyNetworkData
{
    internal const string Queue = "kl009-owned-queue";
    internal const string Message = "kl009-owned-message";
    internal const string Payload = "{\"work\":1}";
    internal const string Headers = "{\"kind\":\"kl009\"}";
    internal const string Healthy = "kl009-healthy-message";
    internal static async Task PrepareAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsPhaseFaultIdentity identity, CancellationToken token)
    {
        var resource = new ResourceDefinition(Queue, ResourceKind.WorkQueue, identity.Partition.TransactionDomainId);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new ConfigureResourceRequest(identity.Partition.TenantId, identity.Partition.DatabaseId, resource), token));
        var principal = new PrincipalRecord(identity.PrincipalId, identity.Partition.TenantId,
            [new(identity.Partition.DatabaseId, RequestCqrsRf3Protocol.AdminCollection,
                Capability.DocumentsRead | Capability.DocumentsWrite),
             new(identity.Partition.DatabaseId, Queue, Capability.QueuePublish | Capability.QueueInspect)], [])
        { ClusterAdministrator = false, PolicyEpoch = checked(RequestCqrsPhaseFaultProvisioning.InitialPolicyEpoch + 1) };
        var stored = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await Assert.That(stored.ClusterAdministrator).IsFalse();
        await Assert.That(stored.Id).IsEqualTo(identity.PrincipalId);
        await Assert.That(JsonDefaults.Serialize(stored).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
    }
    internal static CommandRequest Command(RequestCqrsPhaseFaultIdentity identity, Guid id)
        => new(id, identity.Partition,
            [new PutDocument(RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId,
                RequestCqrsRf3Protocol.ChangedDocumentJson, ExpectedRevision: 1, ExplicitReplacement: true),
             new EnqueueMessage(Queue, Message, Payload, Headers)]);
}
