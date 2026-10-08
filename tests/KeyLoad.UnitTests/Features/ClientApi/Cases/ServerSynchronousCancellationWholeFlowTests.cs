using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-ROUTE-003/REP-004: real canceled file stages retain original cancellation and allow healthy continuation.</summary>
internal sealed class ServerSynchronousCancellationWholeFlowTests
{
    private const string FilePrefix = "keyload-sync-cancellation-";
    private const string FileSuffix = ".tmp";
    private const string GuidFormat = "N";
    private const string Initial = "original actual file";
    private const string Rejected = "unadmitted write";
    private const string Healthy = " healthy continuation";
    private const string Complete = "original actual file healthy continuation";
    private const int SingleFailure = 1;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CanceledOriginalFileWritePreservesIdentityTokenAndBytesThenHealthyWriteCompletes(bool asynchronousInvocation)
    {
        var path = Path.Combine(Path.GetTempPath(), FilePrefix + Guid.NewGuid().ToString(GuidFormat) + FileSuffix);
        try
        {
            await File.WriteAllTextAsync(path, Initial, TestContext.Current!.Execution.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            OperationCanceledException? original = null;
            void Stage()
            {
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    File.AppendAllText(path, Rejected);
                }
                catch (OperationCanceledException error) { original = error; throw; }
            }
            var failures = new List<Exception>();
            if (asynchronousInvocation)
            {
                await ServerFailureObserver.ObserveAsync(() => { Stage(); return Task.CompletedTask; }, failures);
            }
            else
            { ServerFailureObserver.Observe(Stage, failures); }
            await Assert.That(failures.Count).IsEqualTo(SingleFailure);
            await Assert.That(original).IsNotNull();
            var emitted = Assert.ThrowsExactly<OperationCanceledException>(() => ServerFailureObserver.ThrowIfAny(failures));
            await Assert.That(emitted).IsSameReferenceAs(original);
            await Assert.That(emitted.CancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(await File.ReadAllTextAsync(path, TestContext.Current!.Execution.CancellationToken)).IsEqualTo(Initial);
            var healthyFailures = new List<Exception>();
            ServerFailureObserver.Observe(() => File.AppendAllText(path, Healthy), healthyFailures);
            ServerFailureObserver.ThrowIfAny(healthyFailures);
            await Assert.That(healthyFailures).IsEmpty();
            await Assert.That(await File.ReadAllTextAsync(path, TestContext.Current!.Execution.CancellationToken)).IsEqualTo(Complete);
        }
        finally { File.Delete(path); }
    }
}
