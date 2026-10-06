using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeTargetArgumentTests
{
    private const string DatasetParameter = "dataset";
    private const string InvalidRedisConnection = "not a Redis connection string";
    private const string InvalidPostgresConnection = "Host=invalid;Port=not-a-port";
    private const string RunId = "cb4c5e4f-a2c3-4da1-b557-107109d85c68";
    private const string Image = "native-test-image";

    [Test]
    public async Task AcMp010RedisAndPostgresRejectMissingDatasetBeforeDriverSetup()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var redis = new RedisTarget(InvalidRedisConnection, RunId, Image, UnitBenchmarkOptions.Lifecycle(), UnitBenchmarkOptions.Native());
        var redisError = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
            redis.InitializeAsync(null!, cancellationToken));
        await Assert.That(redisError!.ParamName).IsEqualTo(DatasetParameter);

        await using var postgres = new PostgresTarget(InvalidPostgresConnection, RunId, Image, UnitBenchmarkOptions.Native(), UnitBenchmarkOptions.Lifecycle());
        var postgresError = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
            postgres.InitializeAsync(null!, cancellationToken));
        await Assert.That(postgresError!.ParamName).IsEqualTo(DatasetParameter);
    }

    [Test]
    public async Task AcMp012UninitializedNativeTargetsDisposeWithoutOpeningConnections()
    {
        var redis = new RedisTarget(InvalidRedisConnection, RunId, Image, UnitBenchmarkOptions.Lifecycle(), UnitBenchmarkOptions.Native());
        var rabbit = new RabbitTarget("amqp://invalid", RunId, Image, UnitBenchmarkOptions.Lifecycle());
        var postgres = new PostgresTarget(InvalidPostgresConnection, RunId, Image, UnitBenchmarkOptions.Native(), UnitBenchmarkOptions.Lifecycle());

        await redis.DisposeAsync();
        await rabbit.DisposeAsync();
        await postgres.DisposeAsync();
    }
}
