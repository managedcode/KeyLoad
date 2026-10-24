using KeyLoad.Query.QueryExecution;
using KeyLoad.Server;
using KeyLoad.UnitTests.Messaging;
using KeyLoad.UnitTests.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Authorization;

internal static class RevokedOutcomeAdmissionFlow
{
    internal static async Task RunAsync(bool revoked, CancellationToken token)
    {
        RevokedOutcomeAdmissionFixture? fixture = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new();
            fixture.Initialize();
            await WarmAsync(fixture, revoked, failures, token);
        }, failures);
        if (fixture is not null)
        {
            await RevokedOutcomeAdmissionCold.BothAsync(fixture, failures, token);
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WarmAsync(RevokedOutcomeAdmissionFixture fixture, bool revoked,
        List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(() => RevokedOutcomeAdmissionValues.ReceiptAsync(fixture, fixture.Original,
            fixture.OriginalResult, fixture.OriginalPosition, false), failures);
        await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture,
            () => RevokedOutcomeAdmissionCursor.InitialAsync(fixture, token)), failures);
        await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture,
            () => RevokedOutcomeAdmissionModel.RequireAsync(fixture, false, token)), failures);
        ServerFailureObserver.Observe(() => fixture.Policy(2, true, revoked), failures);
        await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture, () => RevokedOutcomeAdmissionValues.RefusedAsync(fixture,
            revoked ? ErrorCode.Unauthenticated : ErrorCode.PermissionDenied, revoked ? "The credential is unavailable or expired."
                : "The principal cannot perform this operation in this scope.")), failures);
        ServerFailureObserver.Observe(() => fixture.Policy(3, false, false), failures);
        await ServerFailureObserver.ObserveAsync(() => RepairedAsync(fixture, token), failures);
    }
    private static async Task RepairedAsync(RevokedOutcomeAdmissionFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture, () => RevokedOutcomeAdmissionValues.RefusedAsync(fixture,
            ErrorCode.PermissionDenied, "The principal policy changed since this command was evaluated.")), failures);
        await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture, async () =>
        {
            var claims = fixture.Database.Verify<QueryCursorClaims>(fixture.Cursor);
            await Assert.That(claims.SourceEpoch).IsEqualTo(fixture.Store.Read(view => fixture.Database.DocumentEpoch(view,
                fixture.Harness.Partition, RevokedOutcomeAdmissionFixture.Documents)));
            await Assert.That(claims.ReadGeneration).IsEqualTo(fixture.Store.Identity.ReadGeneration);
            await RevokedOutcomeAdmissionCursor.FencedAsync(fixture, token);
        }), failures);
        await ServerFailureObserver.ObserveAsync(() => ObserveAsync(fixture,
            () => RevokedOutcomeAdmissionModel.RequireAsync(fixture, false, token)), failures);
        await ServerFailureObserver.ObserveAsync(() => CreateFreshAsync(fixture), failures);
        await ServerFailureObserver.ObserveAsync(() => HealthyAsync(fixture, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task CreateFreshAsync(RevokedOutcomeAdmissionFixture fixture)
    {
        fixture.Fresh = fixture.Next();
        fixture.FreshPosition = fixture.Store.Position + 1;
        fixture.FreshToken = ExpectedCanonicalCommit.Next(fixture.Database, fixture.Harness.Partition);
        var freshResult = fixture.Database.Apply(fixture.Operation(fixture.Fresh));
        fixture.FreshResult = freshResult;
        fixture.FreshOutcome = fixture.ReadOutcome(fixture.Fresh.CommandId);
        await RevokedOutcomeAdmissionValues.ReceiptAsync(fixture, fixture.Fresh, freshResult, fixture.FreshPosition, true);
    }
    internal static Task HealthyAsync(RevokedOutcomeAdmissionFixture fixture, CancellationToken token)
        => ObserveAsync(fixture, async () =>
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => RevokedOutcomeAdmissionModel.RequireAsync(fixture, true, token), failures);
            await ServerFailureObserver.ObserveAsync(() => RevokedOutcomeAdmissionValues.RefusedAsync(fixture, ErrorCode.PermissionDenied,
                "The principal policy changed since this command was evaluated."), failures);
            await ServerFailureObserver.ObserveAsync(() => RevokedOutcomeAdmissionCursor.FencedAsync(fixture, token), failures);
            await ServerFailureObserver.ObserveAsync(() => RevokedOutcomeAdmissionValues.FreshReplayAsync(fixture), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        });
    internal static async Task ObserveAsync(RevokedOutcomeAdmissionFixture fixture, Func<Task> action)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(action, failures);
        await ServerFailureObserver.ObserveAsync(() => RevokedOutcomeAdmissionValues.ConservedAsync(fixture, image, identity, position), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
