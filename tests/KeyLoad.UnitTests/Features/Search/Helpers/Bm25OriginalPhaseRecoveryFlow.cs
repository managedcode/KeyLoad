using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

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
        owner.BindReplicaIdentity();
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

    internal ReplicatedOperation Capture(Guid? expectedGeneration)
        => Bm25CorpusCapture.Create(owner.Database, Publisher, Subject,
            new(owner.Source.Partition, Collection, Field, Text), Guid.NewGuid(), expectedGeneration,
            owner.Database.EvaluationClock.GetUtcNow().Add(UnitRoutingOptions.Routing().Value.RequestLifetime),
            Work(), UnitExecutionOptions.QueryExecution());

    internal ReplicatedOperation Reissue(ReplicatedOperation original)
        => owner.Database.CreateVerifiedBm25CorpusPublication(Publisher,
            NativeCommandPayload.Read<Bm25CorpusPublicationPhaseCommand>(original), Work());

    internal OperationResult Publish(ReplicatedOperation operation) => owner.Submit(operation);

    internal PrincipalRecord Configure(PrincipalRecord principal)
    {
        var operation = owner.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), Root,
            owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
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

    internal async Task RequireDeniedAsync(ReplicatedOperation original, bool issuerMustDeny)
    {
        var before = Bm25OriginalPhaseRecoveryState.Read(owner);
        if (issuerMustDeny)
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => Reissue(original));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        }
        var submitted = issuerMustDeny ? original : Reissue(original);
        var result = Publish(submitted);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
        await Assert.That(owner.Database.ResolveOutcome(submitted).Error).IsEqualTo(ErrorCode.PermissionDenied);
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
    }

    internal async Task RequireHealthyAsync(ReplicatedOperation operation, OperationResult result)
    {
        await Assert.That(result.Error).IsNull();
        var phase = NativeCommandPayload.Read<Bm25CorpusPublicationPhaseCommand>(operation);
        await Assert.That(result.Get<CommitReceipt>().CommandId).IsEqualTo(operation.Id);
        await Assert.That(phase.Current.PolicyEpoch).IsEqualTo(Reader.PolicyEpoch);
        await Assert.That(phase.Current.Statistics.DocumentCount).IsEqualTo(1);
        await Assert.That(phase.Current.Statistics.TotalLength).IsEqualTo(1L);
        var rows = await new SearchEngine(owner.Database, UnitExecutionOptions.QueryExecution())
            .SearchAsync(Subject, new(owner.Source.Partition, Collection, Field, Text), Token);
        await Assert.That(rows.Single().Document.Reference.Id).IsEqualTo("first");
    }
}
