using System.Collections.Immutable;
using System.Net;
using System.Runtime.CompilerServices;
using Grpc.Core;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeComparisonDiagnosticProjectionTests
{
    private const string PartitionIdentity = "partition";
    private const string Canary = "PRIVATE_PAYLOAD_AND_CREDENTIAL";
    private const string SetupGolden = "KurrentSetupFailure|stage=ReplicaCopy|cause[0]=Syst|frame[0]=KeyL.Thro";
    private const string RedisGolden = "{\"Predicate\":\"None\",\"PrimaryHost\":\"primary.dev.internal\",\"PrimaryPort\":6379,\"ReplicaHost\":\"replica1\",\"ReplicaPort\":6379,\"InfoRole\":\"slave\",\"InfoLink\":\"up\",\"InfoHost\":\"primary.dev.internal\",\"InfoPort\":6379,\"RoleCount\":5,\"RoleName\":\"slave\",\"RoleHost\":\"primary.dev.internal\",\"RolePort\":6379,\"RoleState\":\"connected\"}";
    private const string RedisOverflowGolden = "{\"Predicate\":\"ProjectionOverflow\"}";
    private const string CleanupGolden = "{\"SchemaVersion\":1,\"Stage\":0,\"Outcome\":1,\"Reason\":2,\"Counts\":{\"Tracked\":3,\"Submitted\":2,\"Acknowledged\":1,\"Faulted\":1,\"Pending\":0,\"PeakConcurrency\":2},\"ElapsedMilliseconds\":20000,\"CancellationRequested\":false,\"DeadlineExpired\":false,\"GrpcStatus\":4,\"LaterDisposalFailures\":0}";
    private const string OutboxGolden = "KeyLoadOutboxDiagnostic scenario=DocumentUpdate repetition=3 tail=17 firstAvailable=4 storedRecords=14 storedBytes=8192 activeConsumerCount=1 minimumActiveCheckpoint=5";

    [Test]
    public async Task ConfiguredNativeSetupMetadataHasExactBoundedAsciiProjection()
    {
        var options = Configure(settings =>
        {
            settings.KurrentSetupMaximumCauses = 1;
            settings.KurrentSetupMaximumFramesPerCause = 1;
            settings.KurrentSetupMaximumIdentifierCharacters = 4;
            settings.KurrentSetupBuilderCapacity = 1;
        });
        var formatter = new KurrentSetupDiagnostics(options);
        var failure = Capture(() => ThrowNested(4));
        var line = formatter.Format(KurrentSetupStage.ReplicaCopy, failure);
        await Assert.That(line).IsEqualTo(SetupGolden);
        await Assert.That(line.Contains(Canary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(formatter.SafeIdentifier("é\nxxxxxxxx")).IsEqualTo("__xx");
        await Assert.That(formatter.SafeIdentifier(null)).IsEqualTo("unkn");
    }

    [Test]
    public async Task RedisConfiguredProjectionCapKeepsNativeWhitelistAndExactFallback()
    {
        var primary = new DnsEndPoint("primary.dev.internal", 6379);
        var replica = new DnsEndPoint("replica1", 6379);
        Dictionary<string, string> info = new(StringComparer.Ordinal)
        {
            [RedisNativeProtocol.RoleField] = RedisNativeProtocol.ReplicaRole,
            [RedisNativeProtocol.LinkField] = RedisNativeProtocol.LinkUp,
            [RedisNativeProtocol.MasterHostField] = "primary.dev.internal",
            [RedisNativeProtocol.MasterPortField] = "6379",
            [Canary] = Canary
        };
        RedisResult[] role =
        [
            RedisResult.Create((RedisValue)RedisNativeProtocol.ReplicaRole), RedisResult.Create((RedisValue)"primary.dev.internal"),
            RedisResult.Create((RedisValue)6379), RedisResult.Create((RedisValue)"connected"), RedisResult.Create((RedisValue)1)
        ];
        var complete = new RedisReplicaDiagnostics(UnitBenchmarkOptions.Diagnostics()).Project(replica, primary, info, role);
        await Assert.That(complete).IsEqualTo(RedisGolden);
        await Assert.That(complete.Contains(Canary, StringComparison.Ordinal)).IsFalse();
        var lower = Configure(settings => settings.RedisReplicaMaximumCharacters = RedisOverflowGolden.Length);
        var bounded = new RedisReplicaDiagnostics(lower).Project(replica, primary, info, role);
        await Assert.That(bounded).IsEqualTo(RedisOverflowGolden);
        await Assert.That(bounded.Length).IsLessThanOrEqualTo(lower.Value.RedisReplicaMaximumCharacters);
    }

    [Test]
    public async Task ConfiguredCleanupTraversalAndLineCapDoNotExportNativeText()
    {
        var error = new AggregateException(new RpcException(new Status(StatusCode.DeadlineExceeded, Canary)));
        var rootOnly = new KurrentCleanupDiagnostics(Configure(settings => settings.KurrentCleanupMaximumExceptionDepth = 1));
        await Assert.That(rootOnly.Classify(error)).IsEqualTo((KurrentCleanupFailureReason.Unknown, (int?)null));
        var formatter = new KurrentCleanupDiagnostics(Configure(settings => settings.KurrentCleanupMaximumExceptionDepth = 2));
        var classified = formatter.Classify(error);
        await Assert.That(classified).IsEqualTo((KurrentCleanupFailureReason.NativeRpc, (int?)4));
        var diagnostic = new KurrentCleanupDiagnostic(1, KurrentCleanupStage.Delete, KurrentCleanupOutcome.Failed,
            classified.Reason, new KurrentCleanupCounts(3, 2, 1, 1, 0, 2), 20000, false, false, classified.GrpcStatus, 0);
        var json = formatter.Project(diagnostic);
        await Assert.That(json).IsEqualTo(CleanupGolden);
        await Assert.That(json.Contains(Canary, StringComparison.Ordinal)).IsFalse();
        var tooSmall = new KurrentCleanupDiagnostics(Configure(settings => settings.KurrentCleanupMaximumCharacters = json.Length));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Task.FromResult(tooSmall.Project(diagnostic)));
    }

    [Test]
    public async Task ConfiguredOutboxInspectionAndByteLimitsKeepExactPrivateProjection()
    {
        var options = Configure(settings => settings.KeyLoadOutboxMaximumConsumers = 1);
        var formatter = new KeyLoadOutboxDiagnosticLine(options);
        var failed = new ComparisonCase("KeyLoad", Scenario.DocumentUpdate, 3, ComparisonStatuses.Failed, "KeyLoad:ResourceExhausted", null, []);
        var consumer = new ProjectionConsumerInfo(new ProjectionConsumerRef(new PartitionRef("private", "database", "domain", PartitionIdentity), Canary),
            new ProjectionConsumerDefinition(1, [Canary], [Canary]), 5, false);
        var status = new OutboxStatus(new OutboxHead(17, 4, 14, 8192), ImmutableArray.Create(consumer));
        var line = formatter.Format(failed, status);
        await Assert.That(line).IsEqualTo(OutboxGolden);
        await Assert.That(line.Contains(Canary, StringComparison.Ordinal)).IsFalse();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Task.FromResult(formatter.Format(failed, status with { Consumers = status.Consumers.Add(consumer) })));
        var smaller = new KeyLoadOutboxDiagnosticLine(Configure(settings => settings.KeyLoadOutboxMaximumBytes = line.Length - 1));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Task.FromResult(smaller.Format(failed, status)));
    }

    private static IOptions<NativeComparisonDiagnosticOptions> Configure(Action<NativeComparisonDiagnosticOptions> configure)
    {
        var options = UnitBenchmarkOptions.Diagnostics();
        configure(options.Value);
        options.Value.Validate();
        return options;
    }

    private static InvalidOperationException Capture(Action operation)
    {
        try
        { operation(); }
        catch (InvalidOperationException error) { return error; }
        throw new InvalidOperationException("The native fixture did not fail.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNested(int depth)
    {
        if (depth > 0)
        {
            try
            { ThrowNested(depth - 1); }
            catch (Exception error) when (error is InvalidOperationException or DivideByZeroException)
            { throw new InvalidOperationException(Canary, error); }
        }
        else
        { _ = Divide(0); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Divide(int divisor) => 1 / divisor;
}
