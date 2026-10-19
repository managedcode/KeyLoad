using System.Text;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ExecutionPolicyTests
{
    [Test]
    public async Task MissingPolicyFailsBeforeTargetCreation()
    {
        using var client = new HttpClient();
        var missing = Options.Create(new ComparisonExecutionOptions());
        await Assert.That(() => new SurrealDbTarget(client, Guid.NewGuid().ToString(), "test-image", missing)).Throws<OptionsValidationException>();
        await Assert.That(() => new HelixDbVectorTarget(client, "test-image", Guid.NewGuid().ToString(), missing)).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task InconsistentReplyAndIndexLimitsFailValidation()
    {
        var policy = ExecutionPolicyFixture.Read().Value;
        policy.ReadBufferBytes = policy.MaxResponseBytes + 1;
        await Assert.That(() => policy.Validate()).Throws<OptionsValidationException>();
        policy = ExecutionPolicyFixture.Read().Value;
        policy.IndexPollInterval = policy.IndexBuildTimeout;
        await Assert.That(() => policy.Validate()).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task ChunkedBodyCannotExceedConfiguredReplyCeiling()
    {
        var policy = ExecutionPolicyFixture.Read().Value;
        policy.MaxResponseBytes = 16;
        policy.ReadBufferBytes = 8;
        policy.WriteBatchCapacity = 1;
        policy.ReadbackBatchCapacity = 1;
        policy.Validate();
        using var content = new StreamContent(new NonSeekableResponseStream(Encoding.UTF8.GetBytes("{\"rows\":[\"too-large-execution-response\"]}")));
        await Assert.That(content.Headers.ContentLength).IsNull();
        await Assert.That(async () => await ComparisonResponse.ReadJsonAsync(content, policy, TestContext.Current!.Execution.CancellationToken)).Throws<ComparisonFailureException>();
    }

    [Test]
    public async Task IndexEvidenceRetainsEffectiveRuntimePolicy()
    {
        var policy = ExecutionPolicyFixture.Read().Value;
        var evidence = new Dictionary<string, string>();
        policy.RecordEvidence(evidence);
        await Assert.That(evidence[nameof(policy.WriteBatchCapacity)]).IsEqualTo(policy.WriteBatchCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(evidence[nameof(policy.OperationTimeout)]).IsEqualTo(policy.OperationTimeout.ToString("c", System.Globalization.CultureInfo.InvariantCulture));
    }
}
