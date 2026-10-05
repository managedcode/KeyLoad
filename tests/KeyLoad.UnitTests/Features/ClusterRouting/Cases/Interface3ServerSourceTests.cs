using KeyLoad.UnitTests.Features.ClusterRouting.Helpers;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class Interface3ServerSourceTests
{
    private const string Revision = "1e8833c027cf232e35fe012cd3eed41c61a17f89";
    private const string ArchiveName = "interface3-epoch7-server-source.tar";
    private const string InventoryName = "interface3-epoch7-server-source-inventory.json";

    [Test]
    public async Task AcScat004RevalidatesTheActualPinnedArchiveAndProofModuleClosure()
    {
        var result = await Interface3ServerSourceProbe.RunAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.Revision).IsEqualTo(Revision);
        await Assert.That(result.ProofModuleLoaded).IsTrue();
        await Assert.That(result.RepositoryWorkspaceIsActual).IsTrue();
        await Assert.That(result.ExtractedWorkspaceHasNoGit).IsTrue();
        await Assert.That(result.SourceMetadataValid).IsTrue();
        await Assert.That(result.ProtocolMetadataValid).IsTrue();
        await Assert.That(result.ExportRevalidated).IsTrue();
        await Assert.That(result.RetainedArchiveRevalidated).IsTrue();
        await Assert.That(result.RetainedFilesUnchanged).IsTrue();
        await Assert.That(result.SourceCleanupComplete).IsTrue();
        await Assert.That(result.TestRootCleanupComplete).IsTrue();
        await Assert.That(result.ArchiveName).IsEqualTo(ArchiveName);
        await Assert.That(result.InventoryName).IsEqualTo(InventoryName);
        await Assert.That(result.FileCount).IsGreaterThan(0);
        await Assert.That(result.ExpandedBytes).IsGreaterThan(0);
        await Assert.That(result.ArchiveBytes).IsGreaterThan(0);
    }
}
