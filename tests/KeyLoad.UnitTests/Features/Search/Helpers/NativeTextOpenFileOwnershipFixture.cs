using System.Text;
using KeyLoad.Server.Features.Search;
using ZoneTree.AbstractFileStream;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextOpenFileOwnershipFixture
{
    internal static readonly byte[] Held = Encoding.UTF8.GetBytes("original український native handle");
    internal static readonly byte[] Healthy = Encoding.UTF8.GetBytes("fresh healthy bilingual native файл");

    internal static void FlushDurably(IFileStream stream) => stream.Flush(true);

    internal static (NativeTextFileStreamProvider Provider, string HeldPath, string HealthyPath) Create(TestDatabase database)
    {
        var generation = NativeTextOwnershipFixture.CreateGeneration(database,
            TestContext.Current!.Execution.CancellationToken);
        var root = Directory.GetParent(generation)!.FullName;
        var leaf = Path.GetFileName(generation);
        var native = Path.Combine(generation, NativeTextProtocol.NativeDirectory);
        var heldPath = Path.Combine(native, "held-original.bin");
        var healthyPath = Path.Combine(native, "healthy-continuation.bin");
        var original = UnitNativeTextOptions.Execution();
        var initial = new NativeTextFileStreamProvider(root, leaf, database.Store.Identity.NodeId, original);
        using (initial.CreateFileStream(heldPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { }
        using (initial.CreateFileStream(healthyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { }
        var observed = NativeTextFileIO.MeasureRegularFiles(root, original.Value.MaximumFiles,
            original.Value.MaximumDiskBytes, original);
        var options = UnitNativeTextOptions.Execution(new NativeTextExecutionOptions
        { MaximumDiskBytes = checked(observed.Bytes + Held.LongLength + Healthy.LongLength - 1) });
        var resources = new NativeTextResourceOwnership(options);
        resources.RegisterRoot(root, _ => NativeTextRootFiles.VerifyReceipt(root, database.Store.Identity.NodeId, options));
        return (new(root, leaf, database.Store.Identity.NodeId, options, resources), heldPath, healthyPath);
    }
}
