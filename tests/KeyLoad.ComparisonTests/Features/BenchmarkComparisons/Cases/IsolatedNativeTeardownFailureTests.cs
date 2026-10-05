namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedNativeTeardownFailureTests
{
    private const string PrimaryMarker = "private-primary-marker";
    private const string FatalMarker = "private-fatal-marker";

    [Test]
    public async Task WrappedNativeFatalKeepsPriorityAndOtherOriginalFailures()
    {
        var directory = Directory.CreateTempSubdirectory("keyload-native-teardown-fatal-");
        var evidence = Path.Combine(directory.FullName, "evidence");
        var output = Path.Combine(directory.FullName, "output");
        IsolatedNativeTeardownNativeSupport.CreateRawCopyConflict(output, evidence);
        ComparisonProgressNativeFixture? fixture = null;
        var settled = false;
        try
        {
            fixture = await IsolatedNativeTeardownNativeSupport.CreateFixtureAsync(
                Path.Combine(evidence, "progress.json"));
            var fatal = new OutOfMemoryException(FatalMarker);
            var primary = new InvalidOperationException(PrimaryMarker, fatal);
            var failure = await CaptureAsync(() => IsolatedNativeTeardownNativeSupport.CompleteAsync(
                fixture, output, evidence, Path.Combine(directory.FullName, "absent-data"), primary));
            settled = true;
            await Assert.That(ReferenceEquals(fatal, failure.InnerExceptions[0])).IsTrue();
            await Assert.That(failure.InnerExceptions.Any(item => ReferenceEquals(item, primary))).IsTrue();
            await Assert.That(failure.InnerExceptions.Any(item => item is IOException)).IsTrue();
            var receipt = await File.ReadAllTextAsync(Path.Combine(evidence, IsolatedNativeTeardownNativeSupport.ReceiptFile));
            await Assert.That(receipt).DoesNotContain(FatalMarker);
        }
        finally
        {
            if (!settled && fixture is not null)
            {
                await fixture.DisposeAsync();
            }

            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task PrimaryCancellationAndNativeCopyFailureRetainOriginalObjectsAndSafeReceipt()
    {
        var directory = Directory.CreateTempSubdirectory("keyload-native-teardown-failure-");
        var evidence = Path.Combine(directory.FullName, "evidence");
        var output = Path.Combine(directory.FullName, "output");
        var dataRoot = Path.Combine(directory.FullName, "absent-data");
        IsolatedNativeTeardownNativeSupport.CreateRawCopyConflict(output, evidence);
        ComparisonProgressNativeFixture? fixture = null;
        var settled = false;
        try
        {
            fixture = await IsolatedNativeTeardownNativeSupport.CreateFixtureAsync(
                Path.Combine(evidence, "progress.json"));
            using var owner = new CancellationTokenSource();
            var primary = await IsolatedNativeTeardownNativeSupport.CancelNativeRunnerWaitAsync(fixture, owner);
            var primaryToken = primary.CancellationToken;
            var failure = await CaptureAsync(() => IsolatedNativeTeardownNativeSupport.CompleteAsync(
                fixture, output, evidence, dataRoot, primary));
            settled = true;
            await Assert.That(failure.InnerExceptions.Count >= 2).IsTrue();
            await Assert.That(ReferenceEquals(primary, failure.InnerExceptions[0])).IsTrue();
            await Assert.That(primaryToken.IsCancellationRequested).IsTrue();
            await Assert.That(((OperationCanceledException)failure.InnerExceptions[0]).CancellationToken)
                .IsEqualTo(primaryToken);
            await Assert.That(owner.IsCancellationRequested).IsTrue();
            await Assert.That(failure.InnerExceptions.Skip(1).Any(item => item is IOException)).IsTrue();
            var receipt = await File.ReadAllTextAsync(Path.Combine(evidence, IsolatedNativeTeardownNativeSupport.ReceiptFile));
            await Assert.That(receipt).Contains("\"primaryFailure\":true");
            await Assert.That(receipt).Contains("\"raw\"");
            await Assert.That(receipt).DoesNotContain(PrimaryMarker);
            await Assert.That(fixture.Capture.StopAsync().IsCompleted).IsTrue();
        }
        finally
        {
            if (!settled && fixture is not null)
            {
                await fixture.DisposeAsync();
            }

            directory.Delete(recursive: true);
        }
    }

    private static async Task<AggregateException> CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (AggregateException failure)
        {
            return failure;
        }
        throw new InvalidOperationException("Native teardown did not propagate its primary and cleanup failures.");
    }
}
