using System.Text.Json;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class RequiredProcessingPolicyFixture(bool header) : IDisposable
{
    internal const string Queue = "required-input-queue";
    internal const string Derived = "required-input-derived";
    internal const string Worker = "required-input-worker";
    internal const string Before = "required.before.read";
    internal const string After = "required.after.read";
    internal const string Denied = "Required worker input is protected by a missing raw-read grant.";
    internal const string EpochChanged = "The principal policy changed since this command was evaluated.";
    internal const string Unavailable = "The credential is unavailable or expired.";
    internal TestDatabase Harness { get; } = new();
    internal ResourceDefinition Resource { get; private set; } = null!;
    internal List<(Command<DatabaseCommand> Operation, CommitReceipt Receipt, byte[] Outcome)> Producers { get; } = [];
    internal QueueLaneRef Lane => new(Harness.Partition, Queue);
    internal string Payload => header ? "{\"value\":1}" : "{\"secret\":\"BODY-PROCESSING-CANARY\",\"value\":1}";
    internal string Headers => header ? "{\"secret\":\"HEADER-PROCESSING-CANARY\",\"trace\":\"one\"}" : "{\"trace\":\"one\"}";
    internal EntityRef Reference(string id) => new(Harness.Partition, Derived, id);
    internal static string EffectJson(string id) => id == "first" ? "{\"processed\":1}" : "{\"processed\":2}";
    internal ProcessingRequest Processing(Guid id, Delivery delivery, string document)
        => new(id, Lane, delivery.Token, "required-handler", 1L, [new PutDocument(Derived, document, EffectJson(document), 0L)]);
    internal Command<DatabaseCommand> Operation(OperationKind kind, object request, Guid id, string principal = Worker)
        => Harness.Database.CreateCommand(kind, id, principal, Harness.Database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(request, request.GetType(), JsonDefaults.Options));
    internal Result<object> Apply(OperationKind kind, object request, Guid id, string principal = Worker, CancellationToken token = default)
        => Harness.SubmitIssuedEmbedded(Operation(kind, request, id, principal), token);
    internal async Task InitializeAsync(CancellationToken token)
    {
        var policy = new SensitiveFieldPolicy("/secret", "private", Before);
        Resource = Apply(OperationKind.ConfigureResource, new ConfigureResourceRequest(Harness.Partition.TenantId,
            Harness.Partition.DatabaseId, new(Queue, ResourceKind.WorkQueue, Harness.Partition.TransactionDomainId)
            {
                FieldPolicies = header ? [] : [policy],
                HeaderPolicies = header ? [policy] : []
            }), Guid.NewGuid(), "root", token).Get<ResourceDefinition>();
        var derived = new ResourceDefinition(Derived, ResourceKind.Collection, Harness.Partition.TransactionDomainId);
        await RequiredProcessingPolicyAssertions.EqualAsync(derived, Apply(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Harness.Partition.TenantId, Harness.Partition.DatabaseId, derived),
            Guid.NewGuid(), "root", token).Get<ResourceDefinition>());
        await PrincipalAsync(1L, false, token);
        await EnqueueAsync("first", token);
    }
    internal async Task PrincipalAsync(long epoch, bool revoked, CancellationToken token)
    {
        var expected = new PrincipalRecord(Worker, Harness.Partition.TenantId,
            [new(Harness.Partition.DatabaseId, Queue, Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect | Capability.Query),
             new(Harness.Partition.DatabaseId, Derived, Capability.DocumentsWrite | Capability.DocumentsRead)], [Before])
        {
            PolicyEpoch = epoch,
            Revoked = revoked
        };
        await RequiredProcessingPolicyAssertions.EqualAsync(expected,
            Apply(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected), Guid.NewGuid(), "root", token).Get<PrincipalRecord>());
    }
    internal async Task PolicyAsync(string grant, CancellationToken token)
    {
        var policy = new SensitiveFieldPolicy("/secret", "private", grant, RequiredForProcessing: true);
        var expected = Resource with
        {
            SchemaVersion = Resource.SchemaVersion + 1L,
            FieldPolicies = header ? [] : [policy],
            HeaderPolicies = header ? [policy] : []
        };
        Resource = Apply(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Harness.Partition.TenantId, Harness.Partition.DatabaseId, expected)
            {
                ExpectedSchemaVersion = Resource.SchemaVersion
            }, Guid.NewGuid(), "root", token).Get<ResourceDefinition>();
        await RequiredProcessingPolicyAssertions.EqualAsync(expected, Resource);
    }
    internal async Task EnqueueAsync(string id, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), Harness.Partition, [new EnqueueMessage(Queue, id, Payload, Headers)]);
        var operation = Operation(OperationKind.Batch, request, request.CommandId, "root");
        var position = Harness.Store.Position + 1L;
        var expectedToken = Harness.NextCommitToken();
        var receipt = Harness.Database.ApplyEmbedded(operation, token).Get<CommitReceipt>();
        var expected = new CommitReceipt(request.CommandId, expectedToken, [new("enqueue", Queue, id, 1L)], DurabilityProfile.ProcessDurable, 1);
        await RequiredProcessingPolicyAssertions.ReceiptAsync(expected, receipt);
        await Assert.That(Harness.Store.Position).IsEqualTo(position);
        Producers.Add((operation, expected, RequiredProcessingPolicyAssertions.Outcome(Harness.Store, Harness.Partition, "root", request.CommandId)));
    }
    public void Dispose() => Harness.Dispose();
}
