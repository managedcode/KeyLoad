using KeyLoad.Core.ResourceExecution;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.DocumentStorage;

internal sealed class DocumentAdmissionBindingTests
{
    private const string First = "admission-first";
    private const string Second = "admission-second";
    private const string Id = "document";
    private const string Body = "{\"value\":42}";
    private const string Root = "root";

    [Test]
    public async Task EqualLifetimeValuesFromIndependentObjectsKeepAtomicWritesAndReplay()
    {
        using var owner = new TestDatabase();
        owner.Configure(First, ResourceKind.Collection);
        owner.Configure(Second, ResourceKind.Collection);
        var operation = Create(owner);
        var admission = Read(operation);
        await Assert.That(admission.Bindings.Length).IsEqualTo(2);
        Replace(operation, admission with
        {
            Bindings = [.. admission.Bindings.Select(value => value with
            {
                TenantId = new string(value.TenantId.AsSpan()),
                DatabaseId = new string(value.DatabaseId.AsSpan()),
                ResourceName = new string(value.ResourceName.AsSpan()),
                TransactionDomainId = new string(value.TransactionDomainId.AsSpan()),
                Incarnation = new(new string(value.Incarnation.Value.AsSpan()))
            })]
        });

        var first = owner.Database.Apply(operation).Get<CommitReceipt>();
        var replay = owner.Database.Apply(operation).Get<CommitReceipt>();

        await Assert.That(first.Mutations.Length).IsEqualTo(2);
        await Assert.That(replay.Token).IsEqualTo(first.Token);
        await RequireDocumentAsync(owner, First);
        await RequireDocumentAsync(owner, Second);
    }

    [Test]
    [Arguments("order")]
    [Arguments("duplicate")]
    [Arguments("tenant")]
    [Arguments("database")]
    [Arguments("resource")]
    [Arguments("domain")]
    [Arguments("kind")]
    [Arguments("incarnation")]
    public async Task ChangedAdmissionCannotWriteEitherResourceAndHealthyCommandStillCommits(string changed)
    {
        using var owner = new TestDatabase();
        owner.Configure(First, ResourceKind.Collection);
        owner.Configure(Second, ResourceKind.Collection);
        var operation = Create(owner);
        var admission = Read(operation);
        var original = admission.Bindings[0];
        var altered = original with
        {
            TenantId = changed == "tenant" ? "other" : original.TenantId,
            DatabaseId = changed == "database" ? "other" : original.DatabaseId,
            ResourceName = changed == "resource" ? "other" : original.ResourceName,
            TransactionDomainId = changed == "domain" ? "other" : original.TransactionDomainId,
            Kind = changed == "kind" ? ResourceKind.WorkQueue : original.Kind,
            Incarnation = changed == "incarnation" ? new(Guid.NewGuid().ToString("N")) : original.Incarnation
        };
        Replace(operation, admission with
        {
            Bindings = changed switch
            {
                "order" => [.. admission.Bindings.Reverse()],
                "duplicate" => [original, original],
                _ => admission.Bindings.SetItem(0, altered)
            }
        });

        await RequireRefusalAsync(owner, operation, changed);
        await Assert.That(owner.Database.GetDocument(Root, new(owner.Partition, First, Id))).IsNull();
        await Assert.That(owner.Database.GetDocument(Root, new(owner.Partition, Second, Id))).IsNull();
        var healthy = owner.Database.Apply(Create(owner)).Get<CommitReceipt>();
        await Assert.That(healthy.Mutations.Length).IsEqualTo(2);
        await RequireDocumentAsync(owner, First);
        await RequireDocumentAsync(owner, Second);
        await RequireRefusalAsync(owner, operation, changed);
        await RequireDocumentAsync(owner, First);
        await RequireDocumentAsync(owner, Second);
    }

    private static Command<DatabaseCommand> Create(TestDatabase owner)
    {
        var id = Guid.NewGuid();
        return owner.Database.CreateCommand(OperationKind.Batch, id, Root, owner.Database.EvaluationClock.GetUtcNow(),
            new CommandRequest(id, owner.Partition,
                [new PutDocument(First, Id, Body, 0), new PutDocument(Second, Id, Body, 0)]));
    }

    private static ResourceEffectAdmission Read(Command<DatabaseCommand> operation)
        => OrleansSerialization.Deserialize<ResourceEffectAdmission>(operation.Value!.ResourceAdmission.Span);

    private static void Replace(Command<DatabaseCommand> operation, ResourceEffectAdmission admission)
        => operation.Value = operation.Value! with { ResourceAdmission = OrleansSerialization.Serialize(admission) };

    private static async Task RequireRefusalAsync(TestDatabase owner, Command<DatabaseCommand> operation, string changed)
    {
        if (changed == "duplicate")
        {
            var position = owner.Store.Position;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => owner.Database.Apply(operation));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(owner.Store.Position).IsEqualTo(position);
        }
        else
        {
            var rejected = owner.Database.Apply(operation);
            await Assert.That(rejected.IsFailed).IsTrue();
            await Assert.That(Errors.Code(rejected.Problem).Value).IsEqualTo(ErrorCode.TokenInvalidated);
        }
    }

    private static async Task RequireDocumentAsync(TestDatabase owner, string collection)
    {
        var document = owner.Database.GetDocument(Root, new(owner.Partition, collection, Id));
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Json).IsEqualTo(Body);
        await Assert.That(document.Revision).IsEqualTo(1L);
    }
}
