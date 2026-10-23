using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25PrivateAdmissionFlow
{
    private const string Root = "root";
    private const string Subject = "bm25-subject";
    private const string Collection = "orders";
    private const string Document = "healthy";
    private const string Field = "/text";
    private const string Term = "needle";
    private const string OriginalJson = "{\"text\":\"needle\"}";
    private const string ChangedJson = "{\"text\":\"needle changed\"}";

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        try
        {
            using var database = new TestDatabase();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => ExecuteAsync(database), failures);
        }
        catch (Exception error) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase database)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, Document, OriginalJson));
        var subject = database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(
            new(Subject, database.Partition.TenantId,
                [new(database.Partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], [])))
            .Get<PrincipalRecord>();
        var original = Capture(database, Guid.NewGuid(), null, token);
        var phase = OrleansSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((original).Value!.Input.Span);
        var expected = Bm25OriginalPhaseRecoveryState.BeforeLocalPublish(database.Database, database.Partition);
        var result = await SubmitAsync(database, original, token);
        await Bm25PrivateAdmissionAssertions.RequirePublicationAsync(database, phase, result, subject, 1, expected);
        await RequireReplayAsync(database, original, phase, result, token);
        database.Commit(new PutDocument(Collection, Document, ChangedJson, 1, ExplicitReplacement: true));
        var changed = Capture(database, original.CommandId, original.CommandId, token);
        var before = CanonicalStoreCut.Read(database);
        var conflict = await SubmitAsync(database, changed, token);
        await Assert.That(Errors.Code(conflict.Problem)).IsEqualTo(ErrorCode.Conflict);

        await Assert.That(conflict.Value).IsNull();
        await Assert.That(conflict.Problem?.Detail).IsEqualTo("The command ID was already used with different content.");
        await before.RequireAsync(CanonicalStoreCut.Read(database));
        await ReplayResultAssertions.SameAsync(result, database.Database.ResolveSubmissionOutcome(original));
        var healthy = Capture(database, Guid.NewGuid(), original.CommandId, token);
        var healthyExpected = Bm25OriginalPhaseRecoveryState.BeforeLocalPublish(database.Database, database.Partition);
        var healthyResult = await SubmitAsync(database, healthy, token);
        await Bm25PrivateAdmissionAssertions.RequirePublicationAsync(database,
            OrleansSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((healthy).Value!.Input.Span), healthyResult, subject, 2, healthyExpected);
        var document = database.Database.GetDocument(Root, new(database.Partition, Collection, Document));
        await Assert.That(document!.Json).IsEqualTo(ChangedJson);
    }

    private static Command<DatabaseCommand> Capture(TestDatabase database, Guid id, Guid? expected, CancellationToken token)
    {
        var lifetime = UnitRoutingOptions.Routing().Value.RequestLifetime;
        return Bm25CorpusCapture.Create(database.Database, Root, Subject,
            new(database.Partition, Collection, Field, Term), id, expected,
            database.Database.EvaluationClock.GetUtcNow().Add(TimeSpan.FromTicks(lifetime.Ticks / 2)),
            database.CreateReadWork(token), UnitExecutionOptions.QueryExecution());
    }

    private static async Task RequireReplayAsync(TestDatabase database, Command<DatabaseCommand> original,
        Bm25CorpusPublicationPhaseCommand phase, Result<object> result, CancellationToken token)
    {
        var before = CanonicalStoreCut.Read(database);
        var retained = OrleansSerialization.Serialize(database.ReadStoredOutcome(original.CommandId));
        var refreshed = database.Database.CreateVerifiedBm25CorpusPublication(Root, phase, database.CreateReadWork(token));
        var replay = await SubmitAsync(database, refreshed, token);
        await ReplayResultAssertions.SameAsync(result, replay);
        await ReplayResultAssertions.SameAsync(result, database.Database.ResolveSubmissionOutcome(original));
        await before.RequireAsync(CanonicalStoreCut.Read(database));
        await ReplayResultAssertions.BytesAsync(retained,
            OrleansSerialization.Serialize(database.ReadStoredOutcome(original.CommandId)));
        await ReplayResultAssertions.BytesAsync(OrleansSerialization.Serialize(phase),
            OrleansSerialization.Serialize(OrleansSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((database.ReadStoredOutcome(original.CommandId).PreparedCommand!).Value!.Input.Span)));
    }

    private static async Task<Result<object>> SubmitAsync(TestDatabase database, Command<DatabaseCommand> operation,
        CancellationToken token)
    {
        var routing = UnitRoutingOptions.Routing();
        var codec = new KeyLoad.Clustering.GrainRequestCodec(database.Database.Store.Identity.Incarnation,
            database.Database.OperationLimitsOptions, database.Database.EvaluationClock, routing);
        var expiry = database.Database.EvaluationClock.GetUtcNow().Add(routing.Value.RequestLifetime);
        var envelope = codec.CreateBm25CorpusPublication(Guid.NewGuid(), operation, expiry);
        var decoded = codec.Decode(envelope);
        await Assert.That(decoded.Envelope.CommandId).IsEqualTo(operation.CommandId);
        await Assert.That(decoded.Envelope.PrincipalId).IsEqualTo(Root);
        await Assert.That(decoded.Envelope.Purpose).IsEqualTo(KeyLoad.Clustering.Bm25RequestScope.Purpose);
        await Assert.That(decoded.Envelope.ExpiresAt).IsEqualTo(expiry);
        await Assert.That(KeyLoad.Clustering.GrainBm25CorpusPublicationCommand.Resolve(decoded))
            .IsEqualTo(database.Partition.AtomicPartitionId);
        var phase = OrleansSerialization.Deserialize<Bm25CorpusPublicationPhaseCommand>((operation).Value!.Input.Span);
        await Assert.That(decoded.Envelope.ExpiresAt > phase.OriginalExpiry).IsTrue();
        var principal = database.Store.Read(view =>
            database.Database.Principal(view, Root, database.Database.EvaluationClock.GetUtcNow()));
        return await KeyLoad.Clustering.GrainBm25CorpusPublicationCommand.SubmitAsync(database.Database,
            new global::KeyLoad.Core.EmbeddedCoordinator(database.Database), decoded, principal, token);
    }
}
