using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class NativeComparisonHarnessPolicyTests
{
    private const string Stage = "configured-native-settlement";
    private const string CallbackFailure = "native-settlement-callback-failure";

    [Test]
    public async Task AcCq034ConfiguredThresholdJoinsActualCancellationAndRetainsCallbackFailure()
    {
        var policy = new NativeComparisonHarnessOptions { OriginalTaskSettlementTimeout = TimeSpan.FromMilliseconds(10) };
        policy.Validate();
        using var owner = new CancellationTokenSource();
        var callbackFailure = new IOException(CallbackFailure);
        using var callback = owner.Token.Register(() => throw callbackFailure);
        var failures = new IsolatedNativeTeardownFailures(null);
        var original = Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, owner.Token);
        var settlement = IsolatedNativeOriginalTaskSettlement.RunAsync(
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
            { await original; }
            catch (OperationCanceledException) when (original.IsCanceled) { }
            await settlement;
        }
    }

    [Test]
    [Arguments(nameof(NativeComparisonHarnessOptions.OriginalTaskSettlementTimeout), "00:00:00")]
    [Arguments(nameof(NativeComparisonHarnessOptions.TeardownReceiptFileBufferBytes), "4097")]
    public async Task AcCq034InvalidHarnessPolicyFailsNativeValidationBeforeExecution(string setting, string value)
    {
        using var configuration = new ConfigurationManager();
        configuration[setting] = value;
        var options = new OptionsManager<NativeComparisonHarnessOptions>(
            new OptionsFactory<NativeComparisonHarnessOptions>(
                [new ConfigureFromConfigurationOptions<NativeComparisonHarnessOptions>(configuration)], [],
                [new NativeComparisonHarnessOptionsValidator()]));
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => _ = options.Value);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(NativeComparisonHarnessOptions));
    }

    private static AggregateException CaptureFailure(IsolatedNativeTeardownFailures failures)
    {
        try
        { failures.ThrowIfAny(); }
        catch (AggregateException failure) { return failure; }
        throw new InvalidOperationException("The actual native threshold, callback and cancellation failures were not retained.");
    }
}
