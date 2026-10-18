using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class InitialPrincipalEpochFlow
{
    internal static async Task RunAsync(long epoch, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new InitialPrincipalEpochFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.SeedAsync(token);
                var refused = fixture.Configuration(epoch);
                await RefusalAsync(fixture, refused, token);
                var principal = fixture.Configuration(1L);
                await CancelledAsync(fixture, principal);
                await InitialPrincipalEpochAssertions.EqualAsync(fixture.Harness.Database.ApplyEmbedded(principal, token).Get<PrincipalRecord>(),
                    fixture.Principal(1L));
                await InitialPrincipalEpochLifecycle.RunAsync(fixture, token);
                await InitialPrincipalEpochAssertions.ObserveAsync(fixture.Harness.Store, async () =>
                {
                    await InitialPrincipalEpochAssertions.EqualAsync(fixture.Harness.Database.ApplyEmbedded(principal, token).Get<PrincipalRecord>(),
                        fixture.Principal(1L));
                    await InitialPrincipalEpochAssertions.CatalogAsync(fixture, fixture.Harness.Store);
                });
                var state = await InitialPrincipalEpochHealthy.CreateAsync(fixture, token);
                await InitialPrincipalEpochHealthy.RequireAsync(fixture, fixture.Harness.Database, fixture.Harness.Store,
                    state, refused, token);
                await InitialPrincipalEpochCold.RunAsync(fixture, state, refused, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task RefusalAsync(InitialPrincipalEpochFixture fixture, Command<DatabaseCommand> operation, CancellationToken token)
    {
        var store = fixture.Harness.Store;
        var outcome = InitialPrincipalEpochFixture.Outcome(operation);
        var image = InitialPrincipalEpochAssertions.Image(store, outcome);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await InitialPrincipalEpochAssertions.FailureAsync(fixture.Harness.Database.ApplyEmbedded(operation, token),
                Result<object>.Fail(Errors.Problem(ErrorCode.Validation, InitialPrincipalEpochFixture.InvalidDetail)));
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(InitialPrincipalEpochAssertions.Image(store, outcome).SequenceEqual(image)).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(store.Position).IsEqualTo(position + 1L), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(NativeSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(store.Read(view => view.ReadOwnedValue(outcome))).IsNotNull(), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(InitialPrincipalEpochFixture.Reader)))).IsNull(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await InitialPrincipalEpochAssertions.ObserveAsync(store, async () =>
            await InitialPrincipalEpochAssertions.FailureAsync(fixture.Harness.Database.ApplyEmbedded(operation, token),
                Result<object>.Fail(Errors.Problem(ErrorCode.Validation, InitialPrincipalEpochFixture.InvalidDetail))));
    }
    private static Task CancelledAsync(InitialPrincipalEpochFixture fixture, Command<DatabaseCommand> operation)
        => InitialPrincipalEpochAssertions.ObserveAsync(fixture.Harness.Store, async () =>
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var cancelled = new CancellationTokenSource();
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await cancelled.CancelAsync();
                    var failure = Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Harness.Database.ApplyEmbedded(operation, cancelled.Token));
                    await Assert.That(failure.CancellationToken).IsEqualTo(cancelled.Token);
                }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        });
}
