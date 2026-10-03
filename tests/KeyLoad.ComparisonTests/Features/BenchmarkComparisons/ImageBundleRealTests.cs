using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-006/007: trusted build-job native Docker round-trip; selected independently by tree-node path.</summary>
internal sealed class ImageBundleRealTests
{
    [Test]
    public async Task AcIso006NativeArchivesRestoreExactBuiltImagesWithoutRebuilding()
    {
        var result = await ImageBundleRealProcess.RunAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Error).IsEmpty();
        using var document = JsonDocument.Parse(result.Output);
        var proof = document.RootElement;
        await Assert.That(proof.GetProperty(ImageBundleRealProtocol.Source).GetString())
            .IsEqualTo(ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.ShaEnvironment));
        await Assert.That(proof.GetProperty(ImageBundleRealProtocol.Archives).GetInt32()).IsEqualTo(2);
        foreach (var key in ImageBundleRealProtocol.Assertions)
        {
            await Assert.That(proof.GetProperty(key).GetBoolean()).IsTrue();
        }

        var imported = await ComparisonImageReceipt.ReadAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(imported.ServerImage).IsEqualTo(proof.GetProperty(ImageBundleRealProtocol.Server).GetString());
        await Assert.That(imported.RunnerImage).IsEqualTo(proof.GetProperty(ImageBundleRealProtocol.Runner).GetString());
    }
}
