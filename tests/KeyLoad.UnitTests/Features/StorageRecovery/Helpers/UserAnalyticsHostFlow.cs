using P = KeyLoad.UnitTests.Features.StorageRecovery.UserAnalyticsHostProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class UserAnalyticsHostFlow
{
    internal static Task RunAsync(CancellationToken token) => PartitionHostRecoveryFixture.RunAsync(async fixture =>
    {
        UserAnalyticsHostCut? cut = null;
        await fixture.RunHostAsync(async host =>
        {
            await UserAnalyticsHostMutations.BootstrapAsync(fixture, host, token);
            var initial = await UserAnalyticsHostMutations.ApplyAsync(host, UserAnalyticsHostMutations.Initial(),
                AnalyticsStoreProtocol.UpsertKind, P.Second, 1);
            await UserAnalyticsHostRuntimeTrial.RunAsync(fixture, host, async runtime =>
            {
                await UserAnalyticsHostRuntimeTrial.CancelledAsync(host, runtime, token);
                await UserAnalyticsHostRuntimeTrial.AggregateAsync(host, runtime, false, token);
                var update = await UserAnalyticsHostMutations.ApplyAsync(host, UserAnalyticsHostMutations.Update(),
                    AnalyticsStoreProtocol.UpsertKind, P.First, 2);
                var delete = await UserAnalyticsHostMutations.ApplyAsync(host, UserAnalyticsHostMutations.Delete(),
                    AnalyticsStoreProtocol.DeleteKind, P.Second, 3);
                await UserAnalyticsHostRuntimeTrial.AggregateAsync(host, runtime, true, token);
                cut = UserAnalyticsHostAssertions.Capture(host, [initial.Original, update.Original, delete.Original],
                    [initial.Receipt, update.Receipt, delete.Receipt]);
            });
        });
        await fixture.RunHostAsync(async host =>
        {
            await UserAnalyticsHostAssertions.CutAsync(host, cut!);
            foreach (var (original, receipt) in cut!.Originals.Zip(cut.Receipts))
            {
                var actual = host.Database.Apply(original).Get<CommitReceipt>();
                await UserAnalyticsHostAssertions.SameAsync(receipt, actual);
                await Assert.That(actual.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
            }
            await UserAnalyticsHostAssertions.CutAsync(host, cut);
            await UserAnalyticsHostRuntimeTrial.RunAsync(fixture, host,
                runtime => UserAnalyticsHostRuntimeTrial.AggregateAsync(host, runtime, true, token));
            await UserAnalyticsHostAssertions.CutAsync(host, cut);
        });
    });
}
