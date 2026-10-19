using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ComparisonHarnessPolicyTests
{
    private const string Stage = "configured-execution-settlement";
    private const string CallbackFailure = "execution-settlement-callback-failure";

    [Test]
    public async Task AcCq034ConfiguredThresholdJoinsActualCancellationAndRetainsCallbackFailure()
    {
        var policy = new ComparisonHarnessOptions { OriginalTaskSettlementTimeout = TimeSpan.FromMilliseconds(10) };
        policy.Validate();
        using var owner = new CancellationTokenSource();
        var callbackFailure = new IOException(CallbackFailure);
        using var callback = owner.Token.Register(() => throw callbackFailure);
        var failures = new IsolatedTeardownFailures(null);
        var original = Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, owner.Token);
        var settlement = IsolatedOriginalTaskSettlement.RunAsync(
            () => original, Stage, failures, Options.Create(policy), owner.CancelAsync);
        try
        {
            await Assert.That(await settlement.WaitAsync(TimeSpan.FromSeconds(1), TimeProvider.System, TestContext.Current!.Execution.CancellationToken)).IsFalse();
            await Assert.That(original.IsCanceled).IsTrue();
            await Assert.That(owner.IsCancellationRequested).IsTrue();
            var failure = CaptureFailure(failures);
            await Assert.That(failure.InnerExceptions.Any(item => ReferenceEquals(item, callbackFailure)
                || item is AggregateException aggregate && aggregate.InnerExceptions.Any(inner => ReferenceEquals(inner, callbackFailure)))).IsTrue();
            await Assert.That(failure.InnerExceptions.Any(item => item is TimeoutException)).IsTrue();
            await Assert.That(failure.InnerExceptions.OfType<OperationCanceledException>().Any(item => item.CancellationToken == owner.Token)).IsTrue();
        }
        finally
        {
            await owner.CancelAsync();
            try
            {
                await original;
            }
            catch (OperationCanceledException) when (original.IsCanceled) { }
            await settlement;
        }
    }

    [Test]
    [Arguments(nameof(ComparisonHarnessOptions.OriginalTaskSettlementTimeout), "00:00:00")]
    [Arguments(nameof(ComparisonHarnessOptions.TeardownReceiptFileBufferBytes), "4097")]
    public async Task AcCq034InvalidHarnessPolicyFailsValidationBeforeExecution(string setting, string value)
    {
        using var configuration = new ConfigurationManager();
        configuration[setting] = value;
        var options = new OptionsManager<ComparisonHarnessOptions>(
            new OptionsFactory<ComparisonHarnessOptions>(
                [new ConfigureFromConfigurationOptions<ComparisonHarnessOptions>(configuration)], [],
                [new ComparisonHarnessOptionsValidator()]));
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => _ = options.Value);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(ComparisonHarnessOptions));
    }

    private static AggregateException CaptureFailure(IsolatedTeardownFailures failures)
    {
        try
        {
            failures.ThrowIfAny();
        }
        catch (AggregateException failure) { return failure; }
        throw new InvalidOperationException("The actual execution threshold, callback and cancellation failures were not retained.");
    }
}
