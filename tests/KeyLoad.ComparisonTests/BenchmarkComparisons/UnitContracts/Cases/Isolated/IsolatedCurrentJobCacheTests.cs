using System.Text.Json;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-NGR-001 provider captures and cache-policy regressions; fixtures never authenticate a running job.</summary>
internal sealed class IsolatedCurrentJobCacheTests
{
    private const string Result = "result";
    private const string QueuedStates = "queuedStates";
    private const string QueuedIds = "queuedIds";
    private const string QueuedEtags = "queuedEtags";
    private const string CacheControls = "cacheControls";
    private const string RevalidationHeader = "revalidationHeader";
    private const string GenericHeaderAbsent = "genericHeaderAbsent";
    private const string WrongRouteRejected = "wrongRouteRejected";
    private const string RunningState = "runningState";
    private const string RunningId = "runningId";
    private const string ConstructedSameIdentity = "constructedSameIdentity";
    private const string ConstructedSameId = "constructedSameId";
    private const string WrongIdRejected = "wrongIdRejected";
    private const string WrongRunRejected = "wrongRunRejected";
    private const string WrongAttemptRejected = "wrongAttemptRejected";
    private const string WrongSourceRejected = "wrongSourceRejected";
    private const string WrongNameRejected = "wrongNameRejected";
    private const string WrongWorkflowRejected = "wrongWorkflowRejected";
    private const string WrongBranchRejected = "wrongBranchRejected";
    private const string WrongUrlRejected = "wrongUrlRejected";
    private const string TerminalRejected = "terminalRejected";
    private const string Rejected = "rejected";
    private const string Ready = "ready";
    private const string Marker = "marker";

    [Test]
    public async Task AcNgr001OriginalProviderCapturesRemainQueuedWithIdenticalCacheEvidence()
    {
        var result = await IsolatedCurrentJobCacheNodeProbe.RunPolicyAsync(
            TestContext.Current!.Execution.CancellationToken);
        using var document = RequireSuccess(result);
        var value = document.RootElement.GetProperty(Result);
        await Assert.That(value.GetProperty(QueuedStates).EnumerateArray().Select(item => item.GetString()))
            .IsEquivalentTo(new string?[] { "queued", "queued", "queued" }, CollectionOrdering.Matching);
        await Assert.That(value.GetProperty(QueuedIds).EnumerateArray().Select(item => item.GetInt64()).Distinct().Count())
            .IsEqualTo(1);
        await Assert.That(value.GetProperty(QueuedEtags).EnumerateArray().Select(item => item.GetString()).Distinct().Count())
            .IsEqualTo(1);
        await Assert.That(value.GetProperty(CacheControls).EnumerateArray().Select(item => item.GetString()))
            .IsEquivalentTo(Enumerable.Repeat<string?>("private, max-age=60, s-maxage=60", 3), CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcNgr001OnlyExactCurrentJobGetsRevalidationAndIdentityChecksStayStrict()
    {
        var result = await IsolatedCurrentJobCacheNodeProbe.RunPolicyAsync(
            TestContext.Current!.Execution.CancellationToken);
        using var document = RequireSuccess(result);
        var value = document.RootElement.GetProperty(Result);
        await Assert.That(value.GetProperty(RevalidationHeader).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(GenericHeaderAbsent).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(WrongRouteRejected).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(RunningState).GetString()).IsEqualTo("running");
        await Assert.That(value.GetProperty(QueuedIds)[0].GetInt64())
            .IsNotEqualTo(value.GetProperty(RunningId).GetInt64());
        await Assert.That(value.GetProperty(ConstructedSameIdentity).EnumerateArray().Select(item => item.GetString()))
            .IsEquivalentTo(new string?[] { "queued", "running" }, CollectionOrdering.Matching);
        await Assert.That(value.GetProperty(ConstructedSameId).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(WrongIdRejected).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(WrongRunRejected).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(WrongAttemptRejected).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(WrongSourceRejected).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(WrongNameRejected).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(WrongWorkflowRejected).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(WrongBranchRejected).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(WrongUrlRejected).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(TerminalRejected).GetBoolean()).IsTrue();
    }

    [Test]
    public async Task AcNgr001CancellationJoinsWaitAndTheOriginalNativeChild()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var wait = await IsolatedCurrentJobCacheNodeProbe.RunRefreshWaitCancellationAsync(token);
        using (var document = RequireSuccess(wait))
        {
            await Assert.That(document.RootElement.GetProperty(Result).GetProperty(Rejected).GetBoolean()).IsTrue();
        }
        using var directory = new ImageBundleTestDirectory();
        await VerifyChildStopsAsync(directory.Root, "cancel", 1024, token);
        await VerifyChildStopsAsync(directory.Root, "overflow", 32, token);
    }

    private static async Task VerifyChildStopsAsync(string root, string mode, int maximumBytes,
        CancellationToken cancellationToken)
    {
        var marker = Path.Combine(root, mode + ".terminated");
        var ready = Path.Combine(root, mode + ".ready");
        var output = Path.Combine(root, mode + ".capture");
        var result = await IsolatedCurrentJobCacheNodeProbe.RunOwnedChildAsync(output, marker, ready, mode,
            maximumBytes, cancellationToken);
        using var document = RequireSuccess(result);
        var value = document.RootElement.GetProperty(Result);
        await Assert.That(value.GetProperty(Ready).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(Rejected).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(Marker).GetString()).IsEqualTo("terminated");
    }

    private static JsonDocument RequireSuccess(IsolatedAggregateNodeResult result)
    {
        if (result.ExitCode != 0 || result.Error.Length != 0)
        {
            throw new InvalidOperationException("The original bounded current-job Node probe failed.");
        }
        return JsonDocument.Parse(result.Output);
    }
}
