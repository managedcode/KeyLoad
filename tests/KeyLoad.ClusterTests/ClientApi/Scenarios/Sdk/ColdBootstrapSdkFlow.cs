using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Core.InternalSerialization;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Server;
using ManagedCode.Communication.Commands;

namespace KeyLoad.ClusterTests.ClientApi;

internal static class ColdBootstrapSdkFlow
{
    internal const string Collection = "bootstrap-documents";
    internal const string Document = "original";
    internal const string OriginalJson = """{"bootstrap":1}""";
    internal const string HealthyJson = """{"bootstrap":2}""";
    internal const long FirstRevision = 1;
    internal const long HealthyRevision = 2;
    private const long AbsentRevision = 0;
    private const long InitialPosition = 0;
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
        var resourceCommandId = Guid.NewGuid();
        var resourceRequest = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
            new(Collection, ResourceKind.Collection, partition.TransactionDomainId));
        var configured = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(resourceCommandId,
            resourceRequest, joined.Token));
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
        var reference = new EntityRef(partition, Collection, Document)
        {
            Lifetime = ExpectedLifetime(resourceCommandId, resourceRequest)
        };
        await ColdBootstrapSdkAssertions.RuntimeReceiptAsync(receipt, command, FirstRevision, InitialPosition);
        await ColdBootstrapSdkAssertions.DocumentAtMinimumAsync(sdk, reference, receipt.Token, FirstRevision,
            OriginalJson, joined.Token);
        await ColdBootstrapSdkAssertions.DocumentAsync(sdk, reference, FirstRevision, OriginalJson, joined.Token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, joined.Token));
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
        var changed = command with
        {
            Mutations = [new PutDocument(Collection, Document, HealthyJson, FirstRevision, ExplicitReplacement: true)]
        };
        var rejection = await sdk.CommitAsync(changed, joined.Token).ConfigureAwait(false);
        await Assert.That(rejection.IsSuccess).IsFalse();
        await Assert.That(rejection.Value).IsNull();
        await Assert.That(rejection.Problem?.ErrorCode).IsEqualTo(ErrorCode.Conflict.ToString());
        await Assert.That(rejection.Problem?.Detail).IsEqualTo(ConflictDetail);
        await ColdBootstrapSdkAssertions.DocumentAsync(sdk, reference, FirstRevision, OriginalJson, joined.Token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, joined.Token));
        await Assert.That(OrleansSerialization.Serialize(after).SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
        await HealthyContinuationAsync(sdk, reference, command, changed, receipt, joined.Token);
    }

    private static EntityRefLifetime ExpectedLifetime(Guid commandId, ConfigureResourceRequest request)
    {
        var principalId = PartitionStoreProtocol.AdministratorId;
        var fingerprint = DatabaseCommandFingerprint.Compute(commandId, OperationKind.ConfigureResource,
            principalId, JsonSerializer.Serialize(request, JsonDefaults.Options));
        var original = Command<DatabaseCommand>.Create(nameof(DatabaseCommand),
            new DatabaseCommand(OperationKind.ConfigureResource, OrleansSerialization.Serialize(request), fingerprint), commandId);
        original.UserId = principalId;
        var lifetime = ResourceLifetimeIdentity.Create(original, request.TenantId, request.DatabaseId, request.Definition);
        return new(lifetime.Incarnation.Value);
    }

    private static async Task HealthyContinuationAsync(KeyLoadClient sdk, EntityRef reference,
        CommandRequest original, CommandRequest changed, CommitReceipt originalReceipt,
        CancellationToken token)
    {
        var healthyCommand = changed with
        {
            CommandId = Guid.NewGuid()
        };
        await Assert.That(healthyCommand.CommandId).IsNotEqualTo(original.CommandId);
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(healthyCommand, token));
        await Assert.That(healthy.Mutations.Length).IsEqualTo(OneMutation);
        await Assert.That(healthy.Mutations[MutationIndex].Revision).IsEqualTo(HealthyRevision);
        await ColdBootstrapSdkAssertions.RuntimeReceiptAsync(healthy, healthyCommand, HealthyRevision,
            originalReceipt.Token.Position);
        await ColdBootstrapSdkAssertions.DocumentAtMinimumAsync(sdk, reference, healthy.Token, HealthyRevision,
            HealthyJson, token);
        await ColdBootstrapSdkAssertions.DocumentAsync(sdk, reference, HealthyRevision, HealthyJson, token);
        var originalReplay = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(original, token));
        await Assert.That(OrleansSerialization.Serialize(originalReplay)
            .SequenceEqual(OrleansSerialization.Serialize(originalReceipt))).IsTrue();
        await ColdBootstrapSdkAssertions.DocumentAsync(sdk, reference, HealthyRevision, HealthyJson, token);
    }
}
