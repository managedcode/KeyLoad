using KeyLoad.Core.Search;
using KeyLoad.Query;
using KeyLoad.Query.Search;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class Bm25OriginalPhaseRecoveryFlow(Bm25OriginalPhaseRecoveryOwner owner)
{
    internal const string Publisher = "bm25-recovery-publisher";
    internal const string Subject = "bm25-recovery-subject";
    private const string Root = "root";
    private const string Collection = "orders";
    private const string Field = "/text";
    private const string Text = "needle";
    internal Bm25OriginalPhaseRecoveryOwner Owner => owner;
    internal PrincipalRecord Administrator { get; private set; } = null!;
    internal PrincipalRecord Reader { get; private set; } = null!;
    internal static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;

    internal void Seed()
    {
        owner.Source.Configure(Collection, ResourceKind.Collection);
        owner.Source.Commit(new PutDocument(Collection, "first", "{\"text\":\"needle\"}"));
        Administrator = Configure(new(Publisher, owner.Source.Partition.TenantId, [], [])
        {
            ClusterAdministrator = true
        });
        Reader = Configure(new(Subject, owner.Source.Partition.TenantId,
            [new(owner.Source.Partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], []));
    }

    private ReadExecutionBudget Work() => owner.Source.CreateReadWork(Token);

    internal Command<DatabaseCommand> Capture(Guid? expectedGeneration)
        => Bm25CorpusCapture.Create(owner.Database, Publisher, Subject,
            new(owner.Source.Partition, Collection, Field, Text), Guid.NewGuid(), expectedGeneration,
            owner.Database.EvaluationClock.GetUtcNow().Add(UnitRoutingOptions.Routing().Value.RequestLifetime),
            Work(), UnitExecutionOptions.QueryExecution());

    internal Command<DatabaseCommand> Reissue(Command<DatabaseCommand> original)
        => owner.Database.CreateVerifiedBm25CorpusPublication(Publisher,
            OrleansSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((original).Value!.Input.Span), Work());

    internal Result<object> Publish(Command<DatabaseCommand> operation) => owner.Submit(operation);

    internal PrincipalRecord Configure(PrincipalRecord principal)
    {
        var operation = owner.Database.CreateCommand(OperationKind.ConfigurePrincipal, Guid.NewGuid(), Root,
            owner.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
        return owner.Submit(operation).Get<PrincipalRecord>();
    }

    internal void RevokeAdministrator() => Administrator = Configure(Administrator with
    {
        PolicyEpoch = 2,
        ClusterAdministrator = false
    });

    internal void RepairAdministrator() => Administrator = Configure(Administrator with
    {
        PolicyEpoch = 3,
        ClusterAdministrator = true
    });

    internal void RevokeSubject() => Reader = Configure(Reader with
    {
        PolicyEpoch = 2,
        Grants = [new(owner.Source.Partition.DatabaseId, Collection, Capability.DocumentsRead)]
    });

    internal void RepairSubject() => Reader = Configure(Reader with
    {
        PolicyEpoch = 3,
        Grants = [new(owner.Source.Partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)]
    });

    internal async Task RequireDeniedAsync(Command<DatabaseCommand> original, bool issuerMustDeny)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(owner);
        if (issuerMustDeny)
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => Reissue(original));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        }
        var submitted = issuerMustDeny ? original : Reissue(original);
        var result = Publish(submitted);
        await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(result.Value).IsNull();
        await Assert.That(Errors.Code(owner.Database.ResolveOutcome(submitted).Problem).Value).IsEqualTo(ErrorCode.PermissionDenied);
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
    }

    internal async Task RequireHealthyAsync(Command<DatabaseCommand> operation, Result<object> result)
    {
        await Assert.That(Errors.Code(result.Problem).Value).IsNull();
        var phase = OrleansSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((operation).Value!.Input.Span);
        await Assert.That(result.Get<CommitReceipt>().CommandId).IsEqualTo(operation.CommandId);
        await Assert.That(phase.Current.PolicyEpoch).IsEqualTo(Reader.PolicyEpoch);
        await Assert.That(phase.Current.Statistics.DocumentCount).IsEqualTo(1);
        await Assert.That(phase.Current.Statistics.TotalLength).IsEqualTo(1L);
        var rows = await new SearchEngine(owner.Database, UnitExecutionOptions.QueryExecution())
            .SearchAsync(Subject, new(owner.Source.Partition, Collection, Field, Text), Token);
        await Assert.That(rows.Single().Document.Reference.Id).IsEqualTo("first");
    }
}
