using KeyLoad.Core;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class HeavyRouteAdmissionFlow
{
    internal static HttpAdmissionGovernor Governor() => new(UnitAdmissionOptions.Http(new()
    {
        MaxRequests = 16,
        MaxReservedBytes = 25_000,
        OtherReservedBytes = 0,
        HeavyReadReservedBytes = 16_384,
        MaxBodyBytes = 1_024,
        MaxControlBodyBytes = 256,
        ReservedControlRequests = 2,
        ReservedControlBytes = 20_000
    }));
    internal static async Task RunAsync(string route, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new HeavyRouteAdmissionFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                await WaveAsync(fixture, route, false, token);
                await fixture.PersistAsync(true, 2L);
                await WaveAsync(fixture, route, true, token);
                await fixture.PersistAsync(false, 3L);
                await WaveAsync(fixture, route, false, token);
                await fixture.PersistAsync(true, 4L);
                await WaveAsync(fixture, route, true, token);
                await HeavyRouteAdmissionReads.ReplayAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, token);
                await HeavyRouteAdmissionCold.RunAsync(fixture, route, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WaveAsync(HeavyRouteAdmissionFixture fixture, string route, bool use, CancellationToken token)
    {
        var database = fixture.Harness.Database;
        var store = fixture.Harness.Store;
        var governor = Governor();
        await HeavyRouteAdmissionReads.ObserveAsync(store, async () =>
        {
            using (var held = governor.Begin("/query/ast", 0, token))
            {
                held.Bind(HeavyRouteAdmissionReads.Authenticate(database, store), token);
                var before = governor.Status();
                var refusal = Assert.ThrowsExactly<KeyLoadException>(() => governor.Begin(route, 0, token));
                await Assert.That(refusal.Code).IsEqualTo(ErrorCode.ResourceExhausted);
                await Assert.That(refusal.Message).IsEqualTo("The node, tenant or principal command admission budget is exhausted.");
                await HeavyRouteAdmissionReads.EqualAsync(governor.Status(), before);
                using var control = governor.Begin("/admin/dispatch", 0, token);
                control.Bind(HeavyRouteAdmissionReads.Authenticate(database, store), token);
                await Assert.That(governor.Status().Node.ControlCommands).IsEqualTo(1);
            }
            using (var admitted = governor.Begin(route, 0, token))
            {
                admitted.Bind(HeavyRouteAdmissionReads.Authenticate(database, store), token);
                await Assert.That(governor.Status().Node).IsEqualTo(new CommandAdmissionSnapshot(1, 20_480, 0, 0, 1, 1));
                await Assert.That(governor.Status().VerifiedScopes).IsEqualTo(new CommandAdmissionSnapshot(1, 4_096, 0, 0, 1, 1));
                await HeavyRouteAdmissionReads.PathAsync(fixture, database, store, use, token);
            }
            await Assert.That(governor.Status().Node).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
            await Assert.That(governor.Status().VerifiedScopes).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
        });
    }
}
