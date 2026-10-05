using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class RequestCqrsRf3McpGuardEvidenceTests
{
    [Test]
    public async Task AcCrsDiag002RealGuardWarningIsCapturedBetweenHealthySdkAndMcpCalls()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(RequestCqrsRf3Protocol.ParentDeadline);
        var root = RequestCqrsRf3McpGuardEvidenceScenario.CreatePrivateRoot();
        var (profile, _) = await NodeEpochRf3Profile.CreatePriorAsync(root, deadline.Token).ConfigureAwait(false);
        var images = await RequestCqrsRf3ImageProof.ReadAsync(deadline.Token).ConfigureAwait(false);
        var artifact = await RequestCqrsRf3McpGuardEvidenceScenario.ExecuteAsync(root, profile,
            images.Current, deadline.Token).ConfigureAwait(false);
        await RequestCqrsRf3McpGuardEvidenceAssertions.VerifyAsync(artifact, profile.AdminKey,
            deadline.Token).ConfigureAwait(false);
        Directory.Delete(root, recursive: true);
    }
}
