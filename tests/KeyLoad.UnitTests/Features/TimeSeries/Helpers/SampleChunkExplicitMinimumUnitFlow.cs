using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkExplicitMinimumUnitFlow
{
    private const long NextPosition = 1;
    private const long FirstEpoch = 1;
    private const long RevokedEpoch = 2;
    private const long RepairedEpoch = 3;
    private const string ReaderId = "chunk-minimum-reader";
    private const int ColdWaves = 2;

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        SampleChunkCanonicalFixture? fixture = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new SampleChunkCanonicalFixture(nativeReplicaAdmission: true);
            var commands = await SeedAsync(fixture);
            var minimum = commands.Last().Receipt.Token;
            await SampleChunkExplicitMinimumUnitReads.RunAsync(fixture.Owner.Database, fixture.Owner.Store,
                fixture.Request, minimum, ReaderId, token);
            var reader = Reader(fixture.Owner, FirstEpoch, permitted: true);
            fixture.Owner.Submit(OperationKind.ConfigurePrincipal,
                Reader(fixture.Owner, RevokedEpoch, permitted: false)).Get<PrincipalRecord>();
            await SampleChunkExplicitMinimumUnitReads.PermissionAsync(fixture.Owner.Database, fixture.Owner.Store,
                fixture.Request with
                {
                    MinimumToken = minimum with
                    {
                        Incarnation = Guid.Empty
                    }
                }, ReaderId, token);
            fixture.Owner.Submit(OperationKind.ConfigurePrincipal,
                reader with
                {
                    PolicyEpoch = RepairedEpoch
                }).Get<PrincipalRecord>();
            await SampleChunkExplicitMinimumUnitReads.RunAsync(fixture.Owner.Database, fixture.Owner.Store,
                fixture.Request, minimum, ReaderId, token);
            await ReplayAsync(fixture.Owner.Database, fixture.Owner.Store, fixture, commands, token);
            await ColdAsync(fixture, commands, minimum, token);
        }, failures);
        if (fixture is { } owned)
        {
            ServerFailureObserver.Observe(owned.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static PrincipalRecord Reader(TestDatabase owner, long epoch, bool permitted)
        => new(ReaderId, owner.Partition.TenantId,
            permitted ? [new(owner.Partition.DatabaseId, SampleChunkCanonicalFixture.Set, Capability.SeriesRead)] : [], [])
        {
            PolicyEpoch = epoch
        };

    private static async Task<List<(CommandRequest Request, CommitReceipt Receipt)>> SeedAsync(SampleChunkCanonicalFixture fixture)
    {
        var commands = new List<(CommandRequest Request, CommitReceipt Receipt)>();
        var mutations = new Mutation[]
        {
            new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                fixture.WindowId, SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until),
            new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                SampleChunkCanonicalFixture.Initial, SampleChunkCanonicalFixture.Tags),
            new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
                fixture.WindowId, SampleChunkCanonicalFixture.AppendedRevision)
        };
        var kinds = new[] { SampleChunkProtocol.OpenKind, "appendSamples", SampleChunkProtocol.SealKind };
        var revisions = new[] { SampleChunkCanonicalFixture.FirstRevision,
            SampleChunkCanonicalFixture.InitialSequence, SampleChunkCanonicalFixture.SealedRevision };
        foreach (var mutation in mutations)
        {
            var request = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition, [mutation]);
            var expectedPosition = checked(fixture.Owner.ReadJournalCut().LastIndex + NextPosition);
            var actual = fixture.Owner.Submit(OperationKind.Batch, request, id: request.CommandId).Get<CommitReceipt>();
            var index = commands.Count;
            var expected = new CommitReceipt(request.CommandId,
                new(fixture.Owner.Store.Identity.Incarnation, request.Partition.AtomicPartitionId, expectedPosition, FirstEpoch),
                [new MutationReceipt(kinds[index], SampleChunkCanonicalFixture.Set,
                    SampleChunkCanonicalFixture.Series, revisions[index])], DurabilityProfile.QuorumProcessDurable);
            await SampleChunkExplicitMinimumUnitReads.EqualAsync(expected, actual);
            commands.Add((request, expected));
        }
        fixture.Owner.Submit(OperationKind.ConfigurePrincipal, Reader(fixture.Owner, FirstEpoch, permitted: true))
            .Get<PrincipalRecord>();
        return commands;
    }

    private static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        SampleChunkCanonicalFixture fixture, List<(CommandRequest Request, CommitReceipt Receipt)> commands,
        CancellationToken token)
    {
        foreach (var original in commands)
        {
            await SampleChunkExplicitMinimumUnitReads.ObserveAsync(database, store, fixture.Request,
                SampleChunkCanonicalFixture.Principal, () =>
                {
                    var operation = new ReplicatedOperation(original.Request.CommandId, OperationKind.Batch,
                        SampleChunkCanonicalFixture.Principal, default, System.Text.Json.JsonSerializer.Serialize(
                            original.Request, JsonDefaults.Options));
                    var replay = database.ApplyEmbedded(operation, cancellationToken: token).Get<CommitReceipt>();
                    SampleChunkExplicitMinimumUnitReads.RequireBytes(original.Receipt, replay);
                }, token);
        }
    }

    private static async Task ColdAsync(SampleChunkCanonicalFixture fixture,
        List<(CommandRequest Request, CommitReceipt Receipt)> commands, CommitToken minimum, CancellationToken token)
    {
        fixture.Owner.JoinOwnedResources();
        for (var wave = SampleChunkCanonicalFixture.FirstIndex; wave < ColdWaves; wave++)
        {
            var failures = new List<Exception>();
            ZoneTreeStore? reopened = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reopened = new(new(fixture.Owner.Directory), UnitExecutionOptions.StorageExecution(),
                    UnitExecutionOptions.PointCacheExecution());
                var database = fixture.Owner.CreateConfiguredDatabase(reopened);
                await SampleChunkExplicitMinimumUnitReads.RunAsync(database, reopened, fixture.Request, minimum, ReaderId, token);
                await ReplayAsync(database, reopened, fixture, commands, token);
            }, failures);
            if (reopened is { } owned)
            {
                ServerFailureObserver.Observe(owned.Dispose, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
