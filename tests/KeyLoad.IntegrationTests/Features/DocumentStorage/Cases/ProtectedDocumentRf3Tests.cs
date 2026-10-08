using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

[NotInParallel]
internal sealed class ProtectedDocumentRf3Tests
{
    [Test]
    public async Task ActualRetiredOwnersPreservePublicDocumentSdkMcpQ1CancellationReceiptsAndColdReplay()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(20), TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken,
            deadline.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        ProtectedDocumentRf3Scenario? scenario = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token).ConfigureAwait(false);
            scenario = new(wave);
            await scenario.RunAsync(caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (scenario is { } calls)
        { await ServerFailureObserver.ObserveAsync(() => calls.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } nodes)
        { await ServerFailureObserver.ObserveAsync(() => nodes.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    [Test]
    public async Task ActualUncertainParentUsesSenderRuntimeOutcomeWithoutSecondEffect()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(20), TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken,
            deadline.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        ProtectedDocumentUncertaintyScenario? scenario = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token).ConfigureAwait(false);
            scenario = new(wave);
            await scenario.RunAsync(caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (scenario is { } calls)
        { await ServerFailureObserver.ObserveAsync(() => calls.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } nodes)
        { await ServerFailureObserver.ObserveAsync(() => nodes.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
