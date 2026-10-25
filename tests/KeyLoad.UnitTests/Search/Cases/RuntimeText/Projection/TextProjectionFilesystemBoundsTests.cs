using System.Globalization;
using KeyLoad.Query;
using KeyLoad.Server.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

internal sealed class TextProjectionFilesystemBoundsTests
{
    private const string Collection = "execution-filesystem-bounds";
    private const string DirectoryPrefix = "bound-directory-";

    [Test]
    public async Task ExactDirectoryCountPassesAndExcessPreservesManifestAndCanonicalCut()
    {
        using var database = new TestDatabase();
        var generation = CreateGeneration(database);
        var executionOptions = UnitTextOptions.Execution();
        var execution = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore);
        var existing = Directory.EnumerateDirectories(generation, "*", SearchOption.AllDirectories).Count();
        await Assert.That(existing).IsLessThan(executionOptions.Value.MaximumDirectories);
        for (var index = existing; index < executionOptions.Value.MaximumDirectories; index++)
        {
            Directory.CreateDirectory(Path.Combine(execution, DirectoryPrefix + index.ToString(CultureInfo.InvariantCulture)));
        }
        TextFiles.CheckGenerationBound(generation, executionOptions);
        var manifest = Path.Combine(generation, TextProtocol.ManifestFile);
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var before = await File.ReadAllBytesAsync(manifest, cancellationToken);
        var position = database.Store.Position;
        var excess = Path.Combine(execution, DirectoryPrefix + "excess");
        Directory.CreateDirectory(excess);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => TextFiles.CheckGenerationBound(generation, executionOptions));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Directory.Exists(excess)).IsTrue();
        await Assert.That(await File.ReadAllBytesAsync(manifest, cancellationToken))
            .IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    public async Task ExactDirectoryDepthPassesAndNextLevelIsRejectedWithoutDeletion()
    {
        using var database = new TestDatabase();
        var generation = CreateGeneration(database);
        var executionOptions = UnitTextOptions.Execution();
        var current = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore);
        for (var depth = 1; depth < executionOptions.Value.MaximumDepth; depth++)
        {
            current = Path.Combine(current, DirectoryPrefix + depth.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(current);
        }
        TextFiles.CheckGenerationBound(generation, executionOptions);
        var excess = Path.Combine(current, DirectoryPrefix + "excess");
        Directory.CreateDirectory(excess);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => TextFiles.CheckGenerationBound(generation, executionOptions));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(Directory.Exists(current)).IsTrue();
        await Assert.That(Directory.Exists(excess)).IsTrue();
    }

    [Test]
    public async Task LinkedAncestorIsRejectedBeforeCreatingMissingRootOrTouchingItsTarget()
    {
        using var database = new TestDatabase();
        var target = Path.Combine(database.Directory, "protected-target");
        Directory.CreateDirectory(target);
        var marker = Path.Combine(target, "user-owned.bin");
        byte[] expected = [0x21, 0x34, 0x55];
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await File.WriteAllBytesAsync(marker, expected, cancellationToken);
        var link = Path.Combine(database.Directory, "linked-ancestor");
        Directory.CreateSymbolicLink(link, target);
        var missing = Path.Combine(link, "missing", "execution-text");

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => OpenProjection(missing, database));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(Directory.Exists(Path.Combine(target, "missing"))).IsFalse();
        await Assert.That(new DirectoryInfo(link).LinkTarget).IsNotNull();
        await Assert.That(await File.ReadAllBytesAsync(marker, cancellationToken))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(Directory.EnumerateFileSystemEntries(target).ToArray()).IsEquivalentTo([marker],
            CollectionOrdering.Matching);
    }

    private static void OpenProjection(string path, TestDatabase database)
    {
        using var projection = new RuntimeTextProjection(path, database.Store.Identity.NodeId, UnitTextOptions.Execution());
    }

    private static string CreateGeneration(TestDatabase database)
    {
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var root = Path.Combine(database.Directory, "execution-filesystem");
        using (var projection = new RuntimeTextProjection(root, database.Store.Identity.NodeId, UnitTextOptions.Execution()))
        {
            _ = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).Search("root",
                new(database.Partition, Collection, "/text", "needle"));
        }
        return Directory.EnumerateDirectories(root).Single();
    }
}
