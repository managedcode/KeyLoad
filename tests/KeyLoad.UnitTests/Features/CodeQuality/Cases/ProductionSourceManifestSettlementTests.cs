using System.Diagnostics;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.CodeQuality.Cases;

internal sealed class ProductionSourceManifestSettlementTests
{
    private const string OriginalFailure = "The original source-manifest operation failed.";
    private const string ReaderFailure = "The original source-manifest reader failed.";
    private const string CleanupFailure = "The original source-manifest cleanup failed.";
    private const string MissingRuntimeFailure = "The CLR accepted impossible array dimensions.";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcCq044CompletedWhenAllFaultsAreRetainedExactlyOnce(bool alreadyObserved)
    {
        var operation = new IOException(OriginalFailure);
        var reader = new InvalidDataException(ReaderFailure);
        var original = Task.WhenAll(Task.FromException(operation), Task.FromException(reader));
        var failures = new List<Exception>();
        if (alreadyObserved)
        {
            await ServerFailureObserver.ObserveAsync(() => original, failures);
        }
        using var process = new Process();
        var options = ProductionSourceManifestProcess.CaptureExecutionOptions();

        await ProductionSourceManifestProcessSettlement.JoinAsync(process, original,
            options.Value.ProcessSettlementTimeout, failures);

        await Assert.That(original.IsFaulted).IsTrue();
        await Assert.That(failures.Count).IsEqualTo(2);
        await Assert.That(failures[0]).IsSameReferenceAs(operation);
        await Assert.That(failures[1]).IsSameReferenceAs(reader);
    }

    [Test]
    public async Task AcCq044CompletedOriginalTimeoutRetainsItsIdentityWithoutAnotherDeadlineFailure()
    {
        var timeout = new TimeoutException(OriginalFailure);
        var original = Task.FromException(timeout);
        var failures = new List<Exception> { timeout };
        using var process = new Process();
        var options = ProductionSourceManifestProcess.CaptureExecutionOptions();

        await ProductionSourceManifestProcessSettlement.JoinAsync(process, original,
            options.Value.ProcessSettlementTimeout, failures);

        await Assert.That(failures.Count).IsEqualTo(1);
        var projected = Assert.ThrowsExactly<TimeoutException>(
            () => NativeCoverageImageNodeSettlement.ThrowFailures(failures));
        await Assert.That(projected).IsSameReferenceAs(timeout);
    }

    [Test]
    public async Task AcCq044SingleFatalAggregatePreservesOuterAndSiblingReferences()
    {
        var fatal = RuntimeDimensionFailure();
        var sibling = new IOException(ReaderFailure);
        var original = new AggregateException(fatal, sibling);

        var projected = Assert.ThrowsExactly<AggregateException>(
            () => NativeCoverageImageNodeSettlement.ThrowFailures([original]))!;

        await Assert.That(projected).IsSameReferenceAs(original);
        await Assert.That(projected.InnerExceptions.Count).IsEqualTo(2);
        await Assert.That(projected.InnerExceptions[0]).IsSameReferenceAs(fatal);
        await Assert.That(projected.InnerExceptions[1]).IsSameReferenceAs(sibling);
        await Assert.That(CqrsRuntimeFailures.FindFatal(projected)).IsSameReferenceAs(fatal);
    }

    [Test]
    public async Task AcCq044FatalAggregateAndCleanupRetainEveryOriginalFailure()
    {
        var fatal = RuntimeDimensionFailure();
        var sibling = new IOException(ReaderFailure);
        var original = new AggregateException(new AggregateException(OriginalFailure, fatal), sibling);
        var cleanup = new InvalidOperationException(CleanupFailure);

        var projected = Assert.ThrowsExactly<AggregateException>(
            () => NativeCoverageImageNodeSettlement.ThrowFailures([original, cleanup]))!;

        await Assert.That(projected.InnerExceptions.Count).IsEqualTo(2);
        await Assert.That(projected.InnerExceptions[0]).IsSameReferenceAs(original);
        await Assert.That(projected.InnerExceptions[1]).IsSameReferenceAs(cleanup);
        await Assert.That(original.InnerExceptions[1]).IsSameReferenceAs(sibling);
        await Assert.That(CqrsRuntimeFailures.FindFatal(projected)).IsSameReferenceAs(fatal);
    }

    private static OutOfMemoryException RuntimeDimensionFailure()
    {
        try
        {
            _ = Array.CreateInstance(typeof(byte), int.MaxValue, int.MaxValue);
        }
        catch (OutOfMemoryException failure)
        {
            return failure;
        }
        throw new InvalidOperationException(MissingRuntimeFailure);
    }
}
