using System.Text;
using KeyLoad.Server.Search;
using ZoneTree.AbstractFileStream;

namespace KeyLoad.UnitTests.Search;

internal static class TextOpenFileOwnershipFixture
{
    internal static readonly byte[] Held = Encoding.UTF8.GetBytes("original український execution handle");
    internal static readonly byte[] Healthy = Encoding.UTF8.GetBytes("fresh healthy bilingual execution файл");

    internal static void FlushDurably(IFileStream stream) => stream.Flush(true);

    internal static (TextFileStreamProvider Provider, string HeldPath, string HealthyPath) Create(TestDatabase database)
    {
        var generation = TextOwnershipFixture.CreateGeneration(database,
            TestContext.Current!.Execution.CancellationToken);
        var root = Directory.GetParent(generation)!.FullName;
        var leaf = Path.GetFileName(generation);
        var execution = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore);
        var heldPath = Path.Combine(execution, "held-original.bin");
        var healthyPath = Path.Combine(execution, "healthy-continuation.bin");
        var original = UnitTextOptions.Execution();
        var initial = new TextFileStreamProvider(root, leaf, database.Store.Identity.NodeId, original);
        using (initial.CreateFileStream(heldPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
        }
        using (initial.CreateFileStream(healthyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
        }
        var observed = TextFileIO.MeasureRegularFiles(root, original.Value.MaximumFiles,
            original.Value.MaximumDiskBytes, original);
        var options = UnitTextOptions.Execution(new TextExecutionOptions
        {
            MaximumDiskBytes = checked(observed.Bytes + Held.LongLength + Healthy.LongLength - 1)
        });
        var resources = new TextResourceOwnership(options);
        resources.RegisterRoot(root, _ => TextRootFiles.VerifyReceipt(root, database.Store.Identity.NodeId, options));
        return (new(root, leaf, database.Store.Identity.NodeId, options, resources), heldPath, healthyPath);
    }
}
