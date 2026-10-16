using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

[NotInParallel]
internal sealed class DocumentLifetimeRf3Tests
{
    [Test]
    public async Task ActualIndependentD1DualAuthoritySourceRevalidationCancellationAndTwoColdSdkMcpOwners()
    {
        using var expiry = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, expiry.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token);
            await DocumentLifetimeRf3Flow.RunAsync(wave, caller.Token);
        }, failures);
        if (wave is { } actual)
        {
            if (failures.Count != default)
            {
                actual.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => actual.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    [Test]
    public async Task ActualPublishedMoveDocumentReadRetainsFullSeedSdkMcpAndTwoColdOwners()
    {
        using var expiry = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, expiry.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token);
            await ControlledDocumentLifetimeRf3Flow.RunAsync(wave, caller.Token);
        }, failures);
        if (wave is { } actual)
        {
            if (failures.Count != default)
            {
                actual.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => actual.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
