using KeyLoad.Query;
using KeyLoad.Server.Search;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextProjectionBoundsTests
{
    private const string Collection = "execution-text-bounds";
    private const string TextPath = "/text";
    private const string Query = "needle";
    private const string ExactDiskFile = "bound-disk.bin";

    [Test]
    public async Task FileCountAcceptsExactBoundAndRejectsExcessWithoutRemovingFiles()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var indexRoot = await CreateGenerationAsync(database);
        var generation = FindGeneration(indexRoot);
        var execution = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore);
        var executionOptions = UnitTextOptions.Execution();
        var inventory = TextFileIO.MeasureRegularFiles(generation, executionOptions.Value.MaximumFiles,
            executionOptions.Value.MaximumDiskBytes, executionOptions);
        await Assert.That(inventory.Files).IsLessThan(executionOptions.Value.MaximumFiles);
        var addedFiles = await TextBoundedFilePreparation.AddFilesAsync(execution, inventory.Files, executionOptions.Value.MaximumFiles,
            TestContext.Current!.Execution.CancellationToken);

        TextFiles.CheckGenerationBound(generation, executionOptions);
        var manifestPath = Path.Combine(generation, TextProtocol.ManifestFile);
        var manifestBefore = await File.ReadAllBytesAsync(manifestPath, TestContext.Current!.Execution.CancellationToken);
        var excess = Path.Combine(execution, TextBoundedFilePreparation.BoundFilePrefix + "excess");
        await File.WriteAllBytesAsync(excess, [], TestContext.Current!.Execution.CancellationToken);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => TextFiles.CheckGenerationBound(generation, executionOptions));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(await File.ReadAllBytesAsync(manifestPath, TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(manifestBefore,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(addedFiles.All(File.Exists)).IsTrue();
        await Assert.That(File.Exists(excess)).IsTrue();
    }

    [Test]
    public async Task DiskLimitAcceptsExactSparseLengthAndRejectsOneByteExcess()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var indexRoot = await CreateGenerationAsync(database);
        var generation = FindGeneration(indexRoot);
        var execution = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore);
        var executionOptions = UnitTextOptions.Execution();
        var inventory = TextFileIO.MeasureRegularFiles(generation, executionOptions.Value.MaximumFiles,
            executionOptions.Value.MaximumDiskBytes, executionOptions);
        var remainingBytes = executionOptions.Value.MaximumDiskBytes - inventory.Bytes;
        await Assert.That(remainingBytes > 0).IsTrue();
        var sparseFile = Path.Combine(execution, ExactDiskFile);
        TextBoundedFilePreparation.SetSparseLength(sparseFile, remainingBytes);

        TextFiles.CheckGenerationBound(generation, executionOptions);
        var manifestPath = Path.Combine(generation, TextProtocol.ManifestFile);
        var manifestBefore = await File.ReadAllBytesAsync(manifestPath, TestContext.Current!.Execution.CancellationToken);
        TextBoundedFilePreparation.SetSparseLength(sparseFile, remainingBytes + 1);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => TextFiles.CheckGenerationBound(generation, executionOptions));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(new FileInfo(sparseFile).Length).IsEqualTo(remainingBytes + 1);
        await Assert.That(await File.ReadAllBytesAsync(manifestPath, TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(manifestBefore,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static async Task<string> CreateGenerationAsync(TestDatabase database)
    {
        var root = Path.Combine(database.Directory, "execution-text");
        using var projection = new RuntimeTextProjection(root, database.Store.Identity.NodeId, UnitTextOptions.Execution());
        _ = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root",
            new(database.Partition, Collection, TextPath, Query));
        return root;
    }

    private static string FindGeneration(string root)
        => Directory.EnumerateDirectories(root).Single(path => Path.GetFileName(path)
            .StartsWith(TextProtocol.GenerationPrefix, StringComparison.Ordinal));

}
