using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimescaleTimeSeriesIntensiveSessionTests
{
    private const string ValidSchema = "keyload_tsc_00112233445566778899aabbccddeeff";

    [Test]
    public async Task AcTsi008PrivateDataSourceSettingsMatchTheFrozenPoolAndTimeoutPolicy()
    {
        var settings = TimescaleTimeSeriesIntensiveSession.CreateConnectionSettings(
            "Host=127.0.0.1;Database=keyload;Username=benchmark", ValidSchema, UnitBenchmarkOptions.Native());
        await Assert.That(settings.Timeout).IsEqualTo(30);
        await Assert.That(settings.CommandTimeout).IsEqualTo(30);
        await Assert.That(settings.CancellationTimeout).IsEqualTo(2000);
        await Assert.That(settings.MinPoolSize).IsEqualTo(0);
        await Assert.That(settings.MaxPoolSize).IsEqualTo(16);
        await Assert.That(settings.Pooling).IsTrue();
        await Assert.That(settings.Enlist).IsFalse();
        await Assert.That(settings.Multiplexing).IsFalse();
        await Assert.That(settings.NoResetOnClose).IsFalse();
        await Assert.That(settings.IncludeErrorDetail).IsFalse();
        await Assert.That(settings.LogParameters).IsFalse();
        await Assert.That(settings.SearchPath).IsEqualTo(ValidSchema + ",pg_catalog,pg_temp");
        var searchPath = settings.SearchPath ?? throw new InvalidOperationException("The configured search path is missing.");
        await Assert.That(searchPath.Contains("public", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AcTsi008DataSourceSettingsRejectUnvalidatedSchemaBeforeUse()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            TimescaleTimeSeriesIntensiveSession.CreateConnectionSettings(
                "Host=127.0.0.1;Database=keyload;Username=benchmark", "public", UnitBenchmarkOptions.Native()));
        await Assert.That(ValidSchema.Length).IsEqualTo(44);
    }

    [Test]
    public async Task AcCq034ConfiguredAdapterPolicyReachesNativeNpgsqlSettingsAsync()
    {
        var execution = UnitBenchmarkOptions.Native();
        execution.Value.TimescaleConnectionTimeout = TimeSpan.FromSeconds(7);
        execution.Value.TimescaleCommandTimeout = TimeSpan.FromSeconds(11);
        execution.Value.TimescaleCancellationTimeoutMilliseconds = 1250;
        execution.Value.TimescaleMinimumPoolSize = 1;
        execution.Value.TimescaleMaximumPoolSize = 3;
        execution.Value.Validate();
        var settings = TimescaleTimeSeriesIntensiveSession.CreateConnectionSettings(
            "Host=127.0.0.1;Database=keyload;Username=benchmark", ValidSchema, execution);
        await Assert.That(settings.Timeout).IsEqualTo(7);
        await Assert.That(settings.CommandTimeout).IsEqualTo(11);
        await Assert.That(settings.CancellationTimeout).IsEqualTo(1250);
        await Assert.That(settings.MinPoolSize).IsEqualTo(1);
        await Assert.That(settings.MaxPoolSize).IsEqualTo(3);
        await Assert.That(settings.SearchPath).IsEqualTo(ValidSchema + ",pg_catalog,pg_temp");
    }
}
