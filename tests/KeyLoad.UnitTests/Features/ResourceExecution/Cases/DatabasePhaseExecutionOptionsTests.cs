using System.Globalization;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class DatabasePhaseExecutionOptionsTests
{
    private const int MinimumStripeCounterBytes = (32 * 6 * 16 + 32) * sizeof(long);
    private const string MisspelledMode = "Enable";
    private const string MisspelledReservation = "StrpieCount";
    private const string MisspelledRetryBound = "MaximumCasAttemps";
    private const string UnknownSetting = "UnexpectedSetting";

    [Test]
    public async Task NativeCentralBindingPreservesTheDisabledDefaultAndQualifiedCeilings()
    {
        var options = DatabasePhaseTestComposition.Bind();
        var captured = options.Value;
        var bank = DatabasePhaseTestComposition.Create(options);

        await Assert.That(captured.Enabled).IsFalse();
        await Assert.That(captured.StripeCount).IsEqualTo(4);
        await Assert.That(captured.MaximumCasAttempts).IsEqualTo(4);
        await Assert.That(captured.IsValid()).IsTrue();
        await Assert.That(bank.IsEnabled).IsFalse();
        await Assert.That(bank.Begin()).IsEqualTo(-1L);
        await Assert.That(bank.Capture().Histogram.IsEmpty).IsTrue();
    }

    [Test]
    [Arguments(false, nameof(DatabasePhaseExecutionOptions.StripeCount), -1)]
    [Arguments(true, nameof(DatabasePhaseExecutionOptions.StripeCount), -1)]
    [Arguments(false, nameof(DatabasePhaseExecutionOptions.StripeCount), 0)]
    [Arguments(true, nameof(DatabasePhaseExecutionOptions.StripeCount), 0)]
    [Arguments(false, nameof(DatabasePhaseExecutionOptions.StripeCount), 3)]
    [Arguments(true, nameof(DatabasePhaseExecutionOptions.StripeCount), 3)]
    [Arguments(false, nameof(DatabasePhaseExecutionOptions.StripeCount), 5)]
    [Arguments(true, nameof(DatabasePhaseExecutionOptions.StripeCount), 5)]
    [Arguments(false, nameof(DatabasePhaseExecutionOptions.MaximumCasAttempts), -1)]
    [Arguments(true, nameof(DatabasePhaseExecutionOptions.MaximumCasAttempts), -1)]
    [Arguments(false, nameof(DatabasePhaseExecutionOptions.MaximumCasAttempts), 0)]
    [Arguments(true, nameof(DatabasePhaseExecutionOptions.MaximumCasAttempts), 0)]
    [Arguments(false, nameof(DatabasePhaseExecutionOptions.MaximumCasAttempts), 5)]
    [Arguments(true, nameof(DatabasePhaseExecutionOptions.MaximumCasAttempts), 5)]
    public async Task InvalidNativeSettingsRejectAtCentralStartupBeforeBankCreation(bool enabled, string property, int value)
    {
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => DatabasePhaseTestComposition.Bind(
            (nameof(DatabasePhaseExecutionOptions.Enabled), enabled.ToString(CultureInfo.InvariantCulture)),
            (property, value.ToString(CultureInfo.InvariantCulture))));

        await Assert.That(failure.OptionsName).IsEqualTo(Options.DefaultName);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(DatabasePhaseExecutionOptions));
        await Assert.That(failure.Failures.ToArray()).IsEquivalentTo([DatabasePhaseExecutionOptions.ValidationMessage]);
    }

    [Test]
    [Arguments(MisspelledMode)]
    [Arguments(MisspelledReservation)]
    [Arguments(MisspelledRetryBound)]
    [Arguments(UnknownSetting)]
    public async Task UnknownNativeKeysRejectBeforeConstructingAnOtherwiseValidConfiguredBank(string property)
    {
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => DatabasePhaseTestComposition.Create(
            DatabasePhaseTestComposition.Bind(
                (nameof(DatabasePhaseExecutionOptions.Enabled), bool.TrueString),
                (nameof(DatabasePhaseExecutionOptions.StripeCount), "1"),
                (nameof(DatabasePhaseExecutionOptions.MaximumCasAttempts), "1"),
                (property, "1"))));

        await Assert.That(failure.Message.Contains(property, StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains(typeof(DatabasePhaseExecutionOptions).FullName!, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(nameof(DatabasePhaseExecutionOptions.Enabled), "not-a-boolean", typeof(bool))]
    [Arguments(nameof(DatabasePhaseExecutionOptions.Enabled), "1", typeof(bool))]
    [Arguments(nameof(DatabasePhaseExecutionOptions.StripeCount), "not-an-integer", typeof(int))]
    [Arguments(nameof(DatabasePhaseExecutionOptions.StripeCount), "2147483648", typeof(int))]
    [Arguments(nameof(DatabasePhaseExecutionOptions.MaximumCasAttempts), "4.0", typeof(int))]
    public async Task MalformedNativeValuesRetainTheOriginalBinderConversionFailureBeforeBankConstruction(
        string property, string value, Type nativeType)
    {
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => DatabasePhaseTestComposition.Create(
            DatabasePhaseTestComposition.Bind((property, value))));
        var key = DatabasePhaseExecutionOptions.SectionName + ConfigurationPath.KeyDelimiter + property;

        await Assert.That(failure.Message.Contains(key, StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.Message.Contains(nativeType.FullName!, StringComparison.Ordinal)).IsTrue();
        await Assert.That(failure.InnerException).IsNotNull();
    }

    [Test]
    [Arguments("false")]
    [Arguments("true")]
    [Arguments("4")]
    [Arguments("{}")]
    public async Task ScalarWholeSectionRejectsThroughTheActualCentralRegistrationBeforeBankConstruction(string value)
    {
        KeyValuePair<string, string?>[] input = [new(DatabasePhaseExecutionOptions.SectionName, value)];
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => DatabasePhaseTestComposition.Create(
            DatabasePhaseTestComposition.BindRoot(input)));

        await Assert.That(failure.OptionsName).IsEqualTo(Options.DefaultName);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(DatabasePhaseExecutionOptions));
        await Assert.That(failure.Failures.ToArray()).IsEquivalentTo([DatabasePhaseExecutionOptions.ValidationMessage]);
    }

    [Test]
    [Arguments(-1, 4, "stripeCount")]
    [Arguments(0, 4, "stripeCount")]
    [Arguments(3, 4, "stripeCount")]
    [Arguments(5, 4, "stripeCount")]
    [Arguments(4, -1, "maximumCasAttempts")]
    [Arguments(4, 0, "maximumCasAttempts")]
    [Arguments(4, 5, "maximumCasAttempts")]
    public async Task StandaloneBclBoundaryRejectsBeforeEnabledOrDisabledCounterReservation(
        int stripeCount, int maximumCasAttempts, string parameter)
    {
        _ = RejectConstruction(false, stripeCount, maximumCasAttempts);
        _ = RejectConstruction(true, stripeCount, maximumCasAttempts);
        var disabled = RejectConstruction(false, stripeCount, maximumCasAttempts);
        var enabled = RejectConstruction(true, stripeCount, maximumCasAttempts);

        await Assert.That(disabled.Failure.ParamName).IsEqualTo(parameter);
        await Assert.That(enabled.Failure.ParamName).IsEqualTo(parameter);
        await Assert.That(disabled.Allocated).IsLessThan(MinimumStripeCounterBytes);
        await Assert.That(enabled.Allocated).IsEqualTo(disabled.Allocated);
    }

    private static (ArgumentOutOfRangeException Failure, long Allocated) RejectConstruction(
        bool enabled, int stripeCount, int maximumCasAttempts)
    {
        var started = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            _ = new DatabasePhaseBank(enabled, stripeCount, maximumCasAttempts);
        }
        catch (ArgumentOutOfRangeException failure)
        {
            return (failure, GC.GetAllocatedBytesForCurrentThread() - started);
        }

        throw new InvalidOperationException("An invalid phase bank was admitted.");
    }
}
