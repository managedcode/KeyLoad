using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class RequestCqrsRf3DiagnosticsTests
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(45);

    [Test]
    public Task MalformedAspireLogLinesAreExcludedFromTheBoundedArtifact()
        => RunAsync(RejectMalformedLinesAsync);

    [Test]
    public Task EachAspireNodeRetainsOnlyThirtyTwoClosedRecords()
        => RunAsync(VerifyPerNodeCapAsync);

    [Test]
    public Task SuccessEvidenceWaitsForOriginalSubscriptionsAndIsMemoized()
        => RunAsync(VerifySuccessEvidenceLifecycleAsync);

    private static async Task RunAsync(Func<RequestCqrsRf3DiagnosticsTestScope, CancellationToken, Task> scenario)
    {
        using var deadline = new CancellationTokenSource(TestDeadline);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var scope = new RequestCqrsRf3DiagnosticsTestScope(Guid.NewGuid());
            await ServerFailureObserver.ObserveAsync(() => scenario(scope, deadline.Token), failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => scope.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RejectMalformedLinesAsync(RequestCqrsRf3DiagnosticsTestScope scope,
        CancellationToken cancellationToken)
    {
        await scope.StartAsync(cancellationToken).ConfigureAwait(false);
        scope.EmitMalformedThenOneValidPerNode();
        var artifact = await scope.CompleteAndReadArtifactAsync(cancellationToken).ConfigureAwait(false);
        await RequestCqrsRf3DiagnosticsArtifactAssertions.AssertAsync(artifact, scope.WaveId, 1)
            .ConfigureAwait(false);
    }

    private static async Task VerifyPerNodeCapAsync(RequestCqrsRf3DiagnosticsTestScope scope,
        CancellationToken cancellationToken)
    {
        await scope.StartAsync(cancellationToken).ConfigureAwait(false);
        scope.EmitFortyValidLinesPerNode();
        var artifact = await scope.CompleteAndReadArtifactAsync(cancellationToken).ConfigureAwait(false);
        await RequestCqrsRf3DiagnosticsArtifactAssertions.AssertAsync(artifact, scope.WaveId, 32)
            .ConfigureAwait(false);
    }

    private static async Task VerifySuccessEvidenceLifecycleAsync(RequestCqrsRf3DiagnosticsTestScope scope,
        CancellationToken cancellationToken)
    {
        await scope.StartAsync(cancellationToken).ConfigureAwait(false);
        scope.EmitMalformedThenOneValidPerNode();
        await RequestCqrsRf3DiagnosticsSuccessEvidence.AssertUnavailableBeforeJoinAsync(scope)
            .ConfigureAwait(false);
        var evidence = await RequestCqrsRf3DiagnosticsSuccessEvidence.CompleteAndReadMemoizedAsync(scope,
            cancellationToken).ConfigureAwait(false);
        await RequestCqrsRf3DiagnosticsArtifactAssertions.AssertAsync(evidence.Bytes, scope.WaveId, 1)
            .ConfigureAwait(false);
    }
}
