using System.Text;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class NativeExecutionPolicyTests
{
    [Test]
    public async Task MissingPolicyFailsBeforeNativeTargetCreation()
    {
        using var client = new HttpClient();
        var missing = Options.Create(new NativeComparisonExecutionOptions());
        await Assert.That(() => new SurrealDbTarget(client, Guid.NewGuid().ToString(), "test-image", missing)).Throws<OptionsValidationException>();
        await Assert.That(() => new HelixDbVectorTarget(client, "test-image", Guid.NewGuid().ToString(), missing)).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task InconsistentReplyAndIndexLimitsFailValidation()
    {
        var policy = NativeExecutionPolicyFixture.Read().Value;
        policy.ReadBufferBytes = policy.MaxResponseBytes + 1;
        await Assert.That(() => policy.Validate()).Throws<OptionsValidationException>();
        policy = NativeExecutionPolicyFixture.Read().Value;
        policy.IndexPollInterval = policy.IndexBuildTimeout;
        await Assert.That(() => policy.Validate()).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task ChunkedNativeBodyCannotExceedConfiguredReplyCeiling()
    {
        var policy = NativeExecutionPolicyFixture.Read().Value;
        policy.MaxResponseBytes = 16;
        policy.ReadBufferBytes = 8;
        policy.WriteBatchCapacity = 1;
        policy.ReadbackBatchCapacity = 1;
        policy.Validate();
        using var content = new StreamContent(new NonSeekableNativeResponseStream(Encoding.UTF8.GetBytes("{\"rows\":[\"too-large-native-response\"]}")));
        await Assert.That(content.Headers.ContentLength).IsNull();
        await Assert.That(async () => await NativeComparisonResponse.ReadJsonAsync(content, policy, TestContext.Current!.Execution.CancellationToken)).Throws<ComparisonFailureException>();
    }

    [Test]
    public async Task NativeIndexEvidenceRetainsEffectiveRuntimePolicy()
    {
        var policy = NativeExecutionPolicyFixture.Read().Value;
        var evidence = new Dictionary<string, string>();
        policy.RecordEvidence(evidence);
        await Assert.That(evidence[nameof(policy.WriteBatchCapacity)]).IsEqualTo(policy.WriteBatchCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(evidence[nameof(policy.OperationTimeout)]).IsEqualTo(policy.OperationTimeout.ToString("c", System.Globalization.CultureInfo.InvariantCulture));
    }
}
