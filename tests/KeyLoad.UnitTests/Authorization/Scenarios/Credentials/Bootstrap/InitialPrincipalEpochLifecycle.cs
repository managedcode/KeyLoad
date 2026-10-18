using KeyLoad.Core;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class InitialPrincipalEpochLifecycle
{
    internal static async Task RunAsync(InitialPrincipalEpochFixture fixture, CancellationToken token)
    {
        var database = fixture.Harness.Database;
        var store = fixture.Harness.Store;
        await UpdateConflictAsync(fixture, token);
        var original = fixture.Operation(OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(InitialPrincipalEpochFixture.Credential()));
        await KeyAsync(database, original, token);
        await InitialPrincipalEpochAssertions.ObserveAsync(store, async () =>
        {
            await Assert.That(database.Authenticate(InitialPrincipalEpochFixture.OriginalSecret, database.EvaluationClock.GetUtcNow()))
                .IsEqualTo(InitialPrincipalEpochFixture.Reader);
            await InitialPrincipalEpochAssertions.EqualAsync(database.GetDocument(InitialPrincipalEpochFixture.Reader, fixture.Reference),
                new DocumentResult(fixture.Reference, 1L, "{\"value\":0}", false, []));
        });
        await PrincipalAsync(fixture, 2L, false, false, token);
        await InitialPrincipalEpochAssertions.ObserveAsync(store, () => InitialPrincipalEpochAssertions.RefuseAsync(() =>
            database.GetDocument(InitialPrincipalEpochFixture.Reader, fixture.Reference), ErrorCode.PermissionDenied));
        await PrincipalAsync(fixture, 3L, true, true, token);
        await InitialPrincipalEpochAssertions.ObserveAsync(store, () => InitialPrincipalEpochAssertions.RefuseAsync(() =>
            database.Authenticate(InitialPrincipalEpochFixture.OriginalSecret, database.EvaluationClock.GetUtcNow()), ErrorCode.Unauthenticated));
        await PrincipalAsync(fixture, 4L, true, false, token);
        await KeyRevocationAsync(fixture, token);
        await KeyAsync(database, fixture.Operation(OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(InitialPrincipalEpochFixture.Credential(expired: true))), token);
        await InitialPrincipalEpochAssertions.ObserveAsync(store, () => InitialPrincipalEpochAssertions.RefuseAsync(() =>
            database.Authenticate(InitialPrincipalEpochFixture.OriginalSecret, database.EvaluationClock.GetUtcNow()), ErrorCode.Unauthenticated));
        await KeyAsync(database, fixture.Operation(OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(InitialPrincipalEpochFixture.Credential(replacement: true))), token);
        await InitialPrincipalEpochAssertions.ObserveAsync(store, async () =>
        {
            await KeyAsync(database, original, token);
            await InitialPrincipalEpochAssertions.CatalogAsync(fixture, store);
            await InitialPrincipalEpochAssertions.RefuseAsync(() => database.Authenticate(InitialPrincipalEpochFixture.OriginalSecret,
                database.EvaluationClock.GetUtcNow()), ErrorCode.Unauthenticated);
        });
    }
    private static async Task KeyAsync(DatabaseEngine database, Command<DatabaseCommand> operation, CancellationToken token)
        => await Assert.That(database.ApplyEmbedded(operation, token).Get<bool>()).IsTrue();
    private static async Task PrincipalAsync(InitialPrincipalEpochFixture fixture, long epoch, bool grant, bool revoked, CancellationToken token)
    {
        var expected = fixture.Principal(epoch, grant, revoked);
        var operation = fixture.Operation(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected));
        await InitialPrincipalEpochAssertions.EqualAsync(fixture.Harness.Database.ApplyEmbedded(operation, token).Get<PrincipalRecord>(), expected);
    }
    private static async Task UpdateConflictAsync(InitialPrincipalEpochFixture fixture, CancellationToken token)
    {
        var operation = fixture.Configuration(0L);
        var expected = Result<object>.Fail(Errors.Problem(ErrorCode.RevisionConflict, "A policy update must advance its epoch."));
        await InitialPrincipalEpochAssertions.FailureAsync(fixture.Harness.Database.ApplyEmbedded(operation, token), expected);
        await InitialPrincipalEpochAssertions.EqualAsync(fixture.Harness.Store.Read(view =>
            view.GetRecord<PrincipalRecord>(KeySpace.Principal(InitialPrincipalEpochFixture.Reader))), fixture.Principal(1L));
        await InitialPrincipalEpochAssertions.ObserveAsync(fixture.Harness.Store, async () =>
            await InitialPrincipalEpochAssertions.FailureAsync(fixture.Harness.Database.ApplyEmbedded(operation, token), expected));
    }

    private static async Task KeyRevocationAsync(InitialPrincipalEpochFixture fixture, CancellationToken token)
    {
        var database = fixture.Harness.Database;
        await KeyAsync(database, fixture.Operation(OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(InitialPrincipalEpochFixture.Credential() with
            {
                Revoked = true
            })), token);
        await InitialPrincipalEpochAssertions.ObserveAsync(fixture.Harness.Store, () => InitialPrincipalEpochAssertions.RefuseAsync(() =>
            database.Authenticate(InitialPrincipalEpochFixture.OriginalSecret, database.EvaluationClock.GetUtcNow()), ErrorCode.Unauthenticated));
        await KeyAsync(database, fixture.Operation(OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(InitialPrincipalEpochFixture.Credential())), token);
        await InitialPrincipalEpochAssertions.ObserveAsync(fixture.Harness.Store, async () =>
            await Assert.That(database.Authenticate(InitialPrincipalEpochFixture.OriginalSecret, database.EvaluationClock.GetUtcNow()))
                .IsEqualTo(InitialPrincipalEpochFixture.Reader));
    }

}
