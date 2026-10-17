using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Exercises actual native store owners before and after the retirement lock boundary.</summary>
internal sealed class ReplicaIsolationNativeRetirementTests
{
    [Test]
    public async Task CanonicalStoreLocksDenyRetirementUntilAllOwnersJoinAndLiteralRowsReopen()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new ReplicaIsolationNativeRetirementFixture(TimeProvider.System);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                fixture.Initialize();
                await fixture.VerifyHeldOwnersAsync();
                fixture.CloseOwners();
                await fixture.VerifyRetiredAndReopenedAsync();
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
