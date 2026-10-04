namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NodeEpochRegularFileReplacementTests
{
    private const string RootPrefix = "keyload-node-epoch-file-replacement-";
    private const string ExpectedCode = "FormatUnsupported";

    [Test]
    public async Task AcEpoch012InspectedRegularFileReplacedByFifoIsRejectedWithoutReadingEitherPath()
    {
        var root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source.bin");
        var retained = Path.Combine(root, "retained.bin");
        byte[] original = [0x00, 0x5A, 0x71, 0xFF];
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllBytesAsync(source, original);
            var profile = NodeEpochComponentProfile.Create();
            var result = await NodeEpochRegularFileProcess.RunReplacementProbeAsync(source, retained, profile,
                TestContext.Current!.Execution.CancellationToken);

            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.Error).IsEqualTo(string.Empty);
            await Assert.That(result.Output.Trim()).IsEqualTo(ExpectedCode);
            await Assert.That(File.Exists(source)).IsFalse();
            var retainedBytes = await File.ReadAllBytesAsync(retained);
            await Assert.That(retainedBytes.SequenceEqual(original)).IsTrue();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
