using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class GenerationVisibilityOwnerProjectFlow
{
    internal const string Collection = "visibility-owner-project";
    internal const string Reader = "visibility-owner-project-reader";
    internal const string Project = "visibility-project";
    internal const string Foreign = "visibility-foreign";
    internal const string Own = "a";
    internal const string Shared = "b";
    internal const string Hidden = "c";
    internal const string OwnJson = "{\"kind\":\"owned\"}";
    internal const string SharedJson = "{\"kind\":\"project\"}";
    internal const string HiddenJson = "{\"kind\":\"hidden\"}";
    internal const long Initial = 1;
    internal const long Changed = 2;
    internal const long Restored = 3;
    private const long RemovedEpoch = 2;
    private const long RevokedEpoch = 3;
    private const long RepairedEpoch = 4;
    private const string DatabaseScope = "database";
    private const string Tenant = "tenant";
    private const string PutKind = "putDocument";
    private const string MissingPin = "The original complete owner/project scan has no admitted visibility set.";
    private readonly List<(CommandRequest Command, CommitReceipt Receipt, byte[] Outcome)> original = [];

    private static PrincipalRecord Principal => new(Reader, Tenant,
        [new(DatabaseScope, Collection, Capability.DocumentsRead | Capability.Query)], [])
    {
        OwnerId = Reader,
        Projects = [Project],
        RestrictRows = true,
        PolicyEpoch = Initial
    };

    internal static async Task<GenerationVisibilityOwnerProjectFlow> RunAsync(TestDatabase db,
        QueryEngine queries, RowVisibilityCache cache, CancellationToken token)
    {
        var flow = new GenerationVisibilityOwnerProjectFlow();
        db.Configure(Collection, ResourceKind.Collection);
        await flow.ApplyAsync(db, [new PutDocument(Collection, Own, OwnJson, Access: new(OwnerId: Reader)),
            new PutDocument(Collection, Shared, SharedJson, Access: new(ProjectId: Project)),
            new PutDocument(Collection, Hidden, HiddenJson, Access: new(OwnerId: Foreign, ProjectId: Foreign))],
            [new(PutKind, Collection, Own, Initial), new(PutKind, Collection, Shared, Initial),
                new(PutKind, Collection, Hidden, Initial)], token);
        await GenerationVisibilityFlow.ConfigureAsync(db, Principal);
        await GenerationVisibilityOwnerProjectAssertions.ReadAsync(db.Database, queries, db.Store, db.Partition,
            [Own, Shared], Initial, Initial, token);
        using var held = Hold(db, cache);
        await Assert.That(held.Contains(Own)).IsTrue();
        await Assert.That(held.Contains(Shared)).IsTrue();
        await Assert.That(held.Contains(Hidden)).IsFalse();
        await flow.TransitionsAsync(db, queries, cache, held, token);
        await flow.VerifyAsync(db.Database, db.Store, token);
        return flow;
    }

    private async Task TransitionsAsync(TestDatabase db, QueryEngine queries, RowVisibilityCache cache,
        RowVisibilityLease held, CancellationToken token)
    {
        await ApplyAsync(db, [new PutDocument(Collection, Own, OwnJson, Initial,
            new(OwnerId: Foreign, ProjectId: Foreign))], [new(PutKind, Collection, Own, Changed)], token);
        await GenerationVisibilityOwnerProjectAssertions.ReadAsync(db.Database, queries, db.Store, db.Partition,
            [Shared], Changed, Initial, token);
        await Assert.That(held.Contains(Own)).IsTrue();
        using var negative = Hold(db, cache);
        await Assert.That(negative.Contains(Own)).IsFalse();
        await GenerationVisibilityFlow.ConfigureAsync(db, Principal with
        {
            Projects = [],
            PolicyEpoch = RemovedEpoch
        });
        await GenerationVisibilityOwnerProjectAssertions.ReadAsync(db.Database, queries, db.Store, db.Partition,
            [], Changed, Initial, token);
        await Assert.That(held.Contains(Shared)).IsTrue();
        await GenerationVisibilityFlow.ConfigureAsync(db, Principal with
        {
            Revoked = true,
            PolicyEpoch = RevokedEpoch
        });
        await GenerationVisibilityOwnerProjectAssertions.RevokedAsync(db.Database, queries, db.Store, db.Partition, token);
        await GenerationVisibilityFlow.ConfigureAsync(db, Principal with
        {
            PolicyEpoch = RepairedEpoch
        });
        await GenerationVisibilityOwnerProjectAssertions.ReadAsync(db.Database, queries, db.Store, db.Partition,
            [Shared], Changed, Initial, token);
        using var positive = Hold(db, cache);
        await ApplyAsync(db, [new PutDocument(Collection, Own, OwnJson, Changed,
            new(OwnerId: Reader, ProjectId: Foreign))], [new(PutKind, Collection, Own, Restored)], token);
        await GenerationVisibilityOwnerProjectAssertions.ReadAsync(db.Database, queries, db.Store, db.Partition,
            [Own, Shared], Restored, Initial, token);
        await Assert.That(negative.Contains(Own)).IsFalse();
        await ApplyAsync(db, [new PutDocument(Collection, Shared, SharedJson, Initial,
            new(OwnerId: Foreign, ProjectId: Foreign))], [new(PutKind, Collection, Shared, Changed)], token);
        await GenerationVisibilityOwnerProjectAssertions.ReadAsync(db.Database, queries, db.Store, db.Partition,
            [Own], Restored, Changed, token);
        await Assert.That(positive.Contains(Shared)).IsTrue();
    }

    private static RowVisibilityLease Hold(TestDatabase db, RowVisibilityCache cache)
        => db.Database.WithQueryView(Reader, db.Partition, Collection,
            (view, principal, resource) => cache.Acquire(db.Database.CaptureRowVisibility(principal, resource,
                db.Partition, resource.Name, db.Database.DocumentEpoch(view, db.Partition, resource.Name)))
                ?? throw new InvalidOperationException(MissingPin));

    private async Task ApplyAsync(TestDatabase db, ImmutableArray<Mutation> mutations, MutationReceipt[] expected,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var command = new CommandRequest(Guid.NewGuid(), db.Partition, mutations);
        var receipt = db.Submit(OperationKind.Batch, command, TeamRowPolicyFlow.Root, command.CommandId).Get<CommitReceipt>();
        await GenerationVisibilityOwnerProjectAssertions.ReceiptAsync(db.Store, command, receipt, expected);
        var outcome = db.Store.Read(view => view.ReadOwnedValue(global::KeyLoad.Core.KeySpace.PartitionOutcome(
            db.Partition, TeamRowPolicyFlow.Root, command.CommandId)))!;
        original.Add((command, receipt, outcome));
    }

    internal async Task VerifyAsync(DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
    {
        var image = TeamRowPolicyAssertions.Image(store);
        var cut = store.Position;
        foreach (var item in original)
        {
            var operation = database.CreateCommand(OperationKind.Batch, item.Command.CommandId, TeamRowPolicyFlow.Root, default, JsonSerializer.Serialize(item.Command, JsonDefaults.Options));
            var replay = database.ApplyEmbedded(operation, cancellationToken: token).Get<CommitReceipt>();
            await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(item.Receipt))).IsTrue();
            await Assert.That(store.Read(view => view.ReadOwnedValue(global::KeyLoad.Core.KeySpace.PartitionOutcome(
                item.Command.Partition, TeamRowPolicyFlow.Root, item.Command.CommandId)))!.AsSpan().SequenceEqual(item.Outcome)).IsTrue();
        }
        await TeamRowPolicyAssertions.UnchangedAsync(store, image, cut);
        var queries = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        await GenerationVisibilityOwnerProjectAssertions.ReadAsync(database, queries, store, original.First().Command.Partition,
            [Own], Restored, Changed, token);
    }
}
