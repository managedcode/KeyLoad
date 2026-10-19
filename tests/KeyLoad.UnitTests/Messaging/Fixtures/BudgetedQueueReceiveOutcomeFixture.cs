using System.Text.Json;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using P = KeyLoad.UnitTests.Features.Messaging.BudgetedQueueReceiveOutcomeProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class BudgetedQueueReceiveOutcomeFixture : IDisposable
{
    private const int FirstMutationIndex = 0;
    private const int SecondMutationIndex = 1;
    internal TestDatabase Database { get; } = new();
    internal ResourceDefinition Resource { get; private set; } = null!;
    internal PrincipalRecord Worker { get; private set; } = null!;
    internal Command<DatabaseCommand> OriginalIntent { get; private set; } = null!;
    internal byte[] OriginalIntentBytes { get; private set; } = null!;
    internal Result<object> Result { get; private set; }
    internal QueueLaneRef Lane => new(Database.Partition, P.Queue);
    private bool header;

    internal static async Task<BudgetedQueueReceiveOutcomeFixture> CreateAsync(bool header, CancellationToken token)
    {
        var fixture = new BudgetedQueueReceiveOutcomeFixture { header = header };
        try
        {
            await fixture.SeedAsync(token);
            return fixture;
        }
        catch (Exception original)
        {
            try
            {
                fixture.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(original, cleanup); }
            throw;
        }
    }

    private async Task SeedAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var policy = new SensitiveFieldPolicy(P.Path, P.Classification, P.BeforeGrant);
        var definition = new ResourceDefinition(P.Queue, ResourceKind.WorkQueue,
            Database.Partition.TransactionDomainId)
        {
            FieldPolicies = header ? [] : [policy],
            HeaderPolicies = header ? [policy] : []
        };
        Resource = Database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Database.Partition.TenantId, Database.Partition.DatabaseId, definition))
            .Get<ResourceDefinition>();
        Worker = new(P.Worker, Database.Partition.TenantId,
            [new(Database.Partition.DatabaseId, P.Queue, Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect)],
            [P.BeforeGrant])
        {
            PolicyEpoch = P.Initial
        };
        await BudgetedQueueReceiveOutcomeAssertions.EqualAsync(Worker, Database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(Worker)).Get<PrincipalRecord>());
        var command = new CommandRequest(Guid.NewGuid(), Database.Partition,
            [new EnqueueMessage(P.Queue, P.First, P.Payload, P.Headers),
                new EnqueueMessage(P.Queue, P.Second, P.Payload, P.Headers)]);
        var original = Database.Database.OwnCommand(Database.Database.CreateCommand(OperationKind.Batch, command.CommandId, P.Root, Database.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(command, JsonDefaults.Options)));
        var originalBytes = OrleansSerialization.Serialize(original);
        var input = OrleansSerialization.Deserialize<CommandRequest>((original).Value!.Input.Span);
        await Assert.That(JsonSerializer.Serialize(input, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(command, JsonDefaults.Options));
        var index = checked(Database.ReadCanonicalCut().AppliedPosition + P.Step);
        var produced = Database.SubmitIssuedEmbedded(original, token).Get<CommitReceipt>();
        await Assert.That(OrleansSerialization.Serialize(original).SequenceEqual(originalBytes)).IsTrue();
        await BudgetedQueueReceiveOutcomeAssertions.EqualAsync(new CommitReceipt(command.CommandId, new(Database.Store.Identity.Incarnation, Database.Partition.AtomicPartitionId, index), [new(P.EnqueueKind, input.Mutations[FirstMutationIndex].Resource, P.First, P.Initial),
                new(P.EnqueueKind, input.Mutations[SecondMutationIndex].Resource, P.Second, P.Initial)], DurabilityProfile.ProcessDurable, 1), produced);
        (OriginalIntent, Result, OriginalIntentBytes) = Receive();
        await BudgetedQueueReceiveOutcomeAssertions.ReceiveAsync(this, OriginalIntent, Result, P.First);
    }

    internal (Command<DatabaseCommand> Operation, Result<object> Result, byte[] SignedIntentBytes) Receive()
    {
        var request = new ReceiveRequest(Guid.NewGuid(), Lane, MaxMessages: P.OneMessage,
            LeaseSeconds: Resource.QueuePolicy.MaxLeaseSeconds);
        var operation = Database.Database.CreateCommand(OperationKind.Receive, request.RequestId, P.Worker,
            Database.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request));
        var signedIntent = OrleansSerialization.Serialize(operation);
        var result = Database.SubmitIssued(operation);
        if (!OrleansSerialization.Serialize(operation).SequenceEqual(signedIntent))
        {
            throw new InvalidOperationException("The original signed receive intent was changed during submission.");
        }
        return (operation, result, signedIntent);
    }

    internal async Task PolicyAsync(bool repaired)
    {
        var policy = new SensitiveFieldPolicy(P.Path, P.Classification, repaired ? P.BeforeGrant : P.AfterGrant);
        var definition = Resource with
        {
            SchemaVersion = checked(Resource.SchemaVersion + P.Step),
            FieldPolicies = header ? [] : [policy],
            HeaderPolicies = header ? [policy] : []
        };
        var request = new ConfigureResourceRequest(Database.Partition.TenantId, Database.Partition.DatabaseId, definition)
        {
            ExpectedSchemaVersion = Resource.SchemaVersion
        };
        Resource = Database.Submit(OperationKind.ConfigureResource, request).Get<ResourceDefinition>();
        await BudgetedQueueReceiveOutcomeAssertions.EqualAsync(definition, Resource);
        var current = Database.Database.Store.Read(view => Database.Database.Principal(view, P.Worker,
            Database.Database.EvaluationClock.GetUtcNow()));
        await BudgetedQueueReceiveOutcomeAssertions.EqualAsync(Worker, current);
    }

    public void Dispose() => Database.Dispose();
}
