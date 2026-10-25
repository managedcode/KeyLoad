using KeyLoad.Server.Search;

namespace KeyLoad.UnitTests.Search;

internal sealed record TextCapturedFileRoster(string Path, TextIncrementalManifest Manifest, TextOwnedPath[] OwnedPaths)
{
    internal static TextCapturedFileRoster Capture(TestDatabase fixture, TextOnlineTestRuntime runtime,
        OnlineTextIndexMaintenanceRequest request, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(fixture.Database.EvaluationClock, token);
        var publication = fixture.Database.ReadOnlineTextOriginalPublication(TextMaintenanceTestValues.Principal, request, budget)
            ?? throw new InvalidOperationException("The actual original canonical online publication is absent.");
        var root = System.IO.Path.Combine(fixture.Directory, TextOnlineRoot.DirectoryName);
        var path = System.IO.Path.Combine(root, publication.Authority.Leaf);
        var manifest = TextIncrementalMetadata.ReadManifest(path, runtime.Options.RuntimeText.Value.MaximumDiskBytes, budget);
        var owner = TextOwnerFiles.ReadOwner(System.IO.Path.Combine(path, TextProtocol.OwnerFile),
            root, publication.Authority.Leaf, request.NodeId, runtime.Options.RuntimeText);
        RequireLiveInventory(path, owner.OwnedPaths, manifest, runtime, budget);
        return new(path, manifest, owner.OwnedPaths);
    }

    internal async Task RequireRetirementAsync(Task originalRetirement, TextCapturedFileRoster successor,
        TestDatabase fixture, TextOnlineTestRuntime runtime, OnlineTextIndexMaintenanceRequest request,
        CancellationToken token)
    {
        await Assert.That(successor.Path).IsNotEqualTo(Path);
        await originalRetirement.WaitAsync(token);
        await Assert.That(Directory.Exists(Path)).IsFalse();
        successor.RequireCurrent(fixture, runtime, request, token);
    }

    internal void RequireCurrent(TestDatabase fixture, TextOnlineTestRuntime runtime,
        OnlineTextIndexMaintenanceRequest request, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(fixture.Database.EvaluationClock, token);
        var publication = fixture.Store.Read(raw =>
        {
            var view = budget.CreateView(raw);
            var principal = fixture.Database.Principal(view, TextMaintenanceTestValues.Principal,
                fixture.Database.EvaluationClock.GetUtcNow());
            return fixture.Database.ReadOnlineTextCurrentPublication(view, principal, request, budget)
                ?? throw new InvalidOperationException("The actual current canonical online publication is absent.");
        });
        var root = System.IO.Path.Combine(fixture.Directory, TextOnlineRoot.DirectoryName);
        if (publication.CommandId != request.CommandId || System.IO.Path.Combine(root, publication.Authority.Leaf) != Path)
        {
            throw new InvalidOperationException("The actual successor canonical online publication changed.");
        }
        RequireOriginal(fixture, runtime, token);
    }

    internal void RequireOriginal(TestDatabase fixture, TextOnlineTestRuntime runtime, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(fixture.Database.EvaluationClock, token);
        var actual = TextIncrementalMetadata.ReadManifest(Path, runtime.Options.RuntimeText.Value.MaximumDiskBytes, budget);
        if (!OrleansSerialization.Serialize(actual).AsSpan().SequenceEqual(OrleansSerialization.Serialize(Manifest)))
        {
            throw new InvalidOperationException("The original retained execution reader manifest changed.");
        }
        RequireLiveInventory(Path, OwnedPaths, Manifest, runtime, budget);
    }

    private static void RequireLiveInventory(string generationPath, TextOwnedPath[] ownedPaths,
        TextIncrementalManifest manifest, TextOnlineTestRuntime runtime, ReadExecutionBudget budget)
    {
        TextInventory.ValidateTrackedLayout(generationPath, ownedPaths, runtime.Options.RuntimeText, budget);
        var runtimePath = System.IO.Path.Combine(generationPath, TextProtocol.RuntimeDirectoryCore);
        var actual = Directory.EnumerateFiles(runtimePath, "*", SearchOption.AllDirectories)
            .Select(path => RuntimeTextPath.RelativePath(generationPath, path)).Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(manifest.Files.Select(file => file.RelativePath)))
        {
            throw new InvalidOperationException("The complete retained execution file roster changed.");
        }
        long bytes = 0;
        foreach (var file in manifest.Files)
        {
            budget.Check();
            var path = System.IO.Path.Combine(generationPath,
                file.RelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            TextFileIO.VerifyRegularFile(path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                runtime.Options.RuntimeText.Value.HashBufferBytes, FileOptions.SequentialScan);
            var length = input.Length;
            var digest = TextDigest.HashBounded(input, length,
                runtime.Options.RuntimeText.Value.MaximumDiskBytes - bytes, budget,
                executionOptions: runtime.Options.RuntimeText);
            if (input.Length != length || length != file.Length || !digest.AsSpan().SequenceEqual(file.Sha256))
            {
                throw new InvalidOperationException("The complete retained execution file bytes changed.");
            }
            bytes += length;
        }
    }
}
