using System.Collections.Concurrent;
using KeyLoad.ClusterTests.StorageReplication;

namespace KeyLoad.ClusterTests.BlobStorage;

internal sealed class FileStartupDiagnosticTests
{
    private const string Cause = "Unhandled exception. KeyLoad.KeyLoadException: Storage admission failed.";
    private const string Frame = "at KeyLoad.Server.PartitionStorageSession.OpenAsync";
    private const int StackDepth = 100;

    [Test]
    public async Task OriginalStorageStartupCauseSurvivesALongShutdownStack()
    {
        var tail = new ConcurrentQueue<string>();
        var causes = new ConcurrentQueue<string>();
        BoundedDiagnosticLog.CaptureLogLine(tail, causes, Cause);
        for (var index = 0; index < StackDepth; index++)
        {
            BoundedDiagnosticLog.CaptureLogLine(tail, causes, Frame);
        }
        await Assert.That(causes.Single()).IsEqualTo(Cause);
        await Assert.That(tail.Contains(Cause)).IsFalse();
    }
}
