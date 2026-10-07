using KeyLoad.Client;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class ColdBootstrapSdkFlow
{
    internal const string Collection = "bootstrap-documents";
    internal const string Document = "original";
    internal const string OriginalJson = """{"bootstrap":1}""";
    internal const string HealthyJson = """{"bootstrap":2}""";
    internal const long FirstRevision = 1;
    internal const long HealthyRevision = 2;
    private const long AbsentRevision = 0;
    private const int OneMutation = 1;
    private const int MutationIndex = 0;
    private const string Tenant = "bootstrap-tenant";
    private const string Database = "bootstrap-database";
    private const string Domain = "bootstrap-domain";
    private const string Bucket = "bootstrap-bucket";
    private const string ConflictDetail = "The command ID was already used with different content.";

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        using var deadline = McpCallerDeadline.Create();
        using var joined = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var partition = new PartitionRef(Tenant, Database, Domain, Bucket);
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var status = await McpCallerAssertions.SdkSuccessAsync(await admin.StatusAsync(joined.Token));
        await ColdBootstrapCliStatus.VerifyAsync(fixture, status, joined.Token).ConfigureAwait(false);
        var configured = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(Collection, ResourceKind.Collection, partition.TransactionDomainId)), joined.Token));
        await Assert.That(configured.Name).IsEqualTo(Collection);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Collection,
            Capability.DocumentsRead | Capability.DocumentsWrite, joined.Token).ConfigureAwait(false);
        await Assert.That(identity.Principal.ClusterAdministrator).IsFalse();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, partition, [new PutDocument(Collection, Document, OriginalJson, ExpectedRevision: AbsentRevision)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, joined.Token));
        await ColdBootstrapSdkAssertions.ReceiptAsync(receipt, command).ConfigureAwait(false);
        var reference = new EntityRef(partition, Collection, Document);
        await ColdBootstrapSdkAssertions.DocumentAsync(sdk, reference, FirstRevision, OriginalJson, joined.Token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, joined.Token));
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        var changed = command with { Mutations = [new PutDocument(Collection, Document, HealthyJson, FirstRevision, ExplicitReplacement: true)] };
        var rejection = await sdk.CommitAsync(changed, joined.Token).ConfigureAwait(false);
        await Assert.That(rejection.IsSuccess).IsFalse();
        await Assert.That(rejection.Value).IsNull();
        await Assert.That(rejection.Problem?.ErrorCode).IsEqualTo(ErrorCode.Conflict.ToString());
        await Assert.That(rejection.Problem?.Detail).IsEqualTo(ConflictDetail);
        await ColdBootstrapSdkAssertions.DocumentAsync(sdk, reference, FirstRevision, OriginalJson, joined.Token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, joined.Token));
        await Assert.That(NativeSerialization.Serialize(after).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(changed with { CommandId = Guid.NewGuid() }, joined.Token));
        await Assert.That(healthy.Mutations.Length).IsEqualTo(OneMutation);
        await Assert.That(healthy.Mutations[MutationIndex].Revision).IsEqualTo(HealthyRevision);
        await ColdBootstrapSdkAssertions.DocumentAsync(sdk, reference, HealthyRevision, HealthyJson, joined.Token);
    }
}
