using System.Collections.Immutable;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.BackupRestore;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25RestoredEligibilityPolicy
{
    internal static async Task<(ReplicatedOperation Operation, CommitReceipt Receipt, Bm25CorpusCurrentV1 Head, byte[] Bytes)>
        RepairAsync(EventingArtifactFixture fixture, Bm25CorpusCurrentV1 originalHead, CancellationToken token)
    {
        var original = fixture.Database.WithQueryView(Bm25PortableFamilyBackupSeed.FirstSubject,
            fixture.Source.Partition, Bm25PortableFamilyBackupSeed.Collection, (_, principal, _) => principal);
        var denied = Set(fixture, original with
        {
            PolicyEpoch = checked(original.PolicyEpoch + 1),
            Grants = original.Grants.Select(grant => grant with { Capabilities = grant.Capabilities & ~Capability.Query }).ToImmutableArray()
        });
        var before = Bm25RestoredEligibilityAssertions.Cut(fixture.Target!, fixture.Database,
            fixture.ReadTargetJournalCut(), token);
        var source = Bm25RestoredEligibilityAssertions.Cut(fixture.Source.Store, fixture.Source.Database,
            fixture.Source.ReadJournalCut(), token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => new SearchEngine(fixture.Database,
                UnitExecutionOptions.QueryExecution()).SearchAsync(original.Id,
                new(fixture.Source.Partition, Bm25PortableFamilyBackupSeed.Collection,
                    Bm25PortableFamilyBackupSeed.Field, "needle"), token));
            await Assert.That(error!.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Bm25RestoredEligibilityAssertions.SameAsync(before,
                Bm25RestoredEligibilityAssertions.Cut(fixture.Target!, fixture.Database, fixture.ReadTargetJournalCut(), token));
            await Bm25RestoredEligibilityAssertions.SameAsync(source,
                Bm25RestoredEligibilityAssertions.Cut(fixture.Source.Store, fixture.Source.Database, fixture.Source.ReadJournalCut(), token));
        }, failures);
        PrincipalRecord? restored = null;
        ServerFailureObserver.Observe(() => restored = Set(fixture,
            original with { PolicyEpoch = checked(denied.PolicyEpoch + 1) }), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(restored!.PolicyEpoch).IsEqualTo(3L);
        await Assert.That(Bm25RestoredEligibilityAssertions.Current(fixture, original.Id)).IsNull();
        var operation = Bm25CorpusCapture.Create(fixture.Database, Bm25PortableFamilyBackupSeed.Root, original.Id,
            new(fixture.Source.Partition, Bm25PortableFamilyBackupSeed.Collection, Bm25PortableFamilyBackupSeed.Field, "needle"),
            Guid.NewGuid(), originalHead.Generation, fixture.Database.EvaluationClock.GetUtcNow()
                .Add(UnitRoutingOptions.Routing().Value.RequestLifetime), fixture.Source.CreateReadWork(token), UnitExecutionOptions.QueryExecution());
        var receipt = fixture.SubmitIssued(fixture.Database, operation, cancellationToken: token).Get<CommitReceipt>();
        var bytes = fixture.Target!.Read(view => view.ReadOwnedValue(Bm25CorpusKeys.Current(originalHead)))!;
        var head = NativeSerialization.Deserialize<Bm25CorpusCurrentV1>(bytes);
        await Assert.That(head.PolicyEpoch).IsEqualTo(restored.PolicyEpoch);
        await Assert.That(head.Incarnation).IsEqualTo(fixture.Target.Identity.Incarnation);
        await Assert.That(head.Generation).IsEqualTo(operation.Id);
        return (operation, receipt, head, bytes);
    }

    private static PrincipalRecord Set(EventingArtifactFixture fixture, PrincipalRecord principal)
        => fixture.Apply(fixture.Database, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal), Guid.NewGuid()).Get<PrincipalRecord>();
}
