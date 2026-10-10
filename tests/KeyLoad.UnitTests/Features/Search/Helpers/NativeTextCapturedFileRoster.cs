using KeyLoad.Core;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed record NativeTextCapturedFileRoster(string Path, NativeTextIncrementalManifest Manifest, NativeTextOwnedPath[] OwnedPaths)
{
    internal static NativeTextCapturedFileRoster Capture(TestDatabase fixture, NativeTextOnlineTestRuntime runtime,
        OnlineTextIndexMaintenanceRequest request, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(runtime.Options.Core.DatabaseLimits, fixture.Database.EvaluationClock, token);
        var publication = fixture.Database.ReadOnlineTextOriginalPublication(NativeTextMaintenanceTestValues.Principal, request, budget)
            ?? throw new InvalidOperationException("The actual original canonical online publication is absent.");
        var root = System.IO.Path.Combine(fixture.Directory, NativeTextOnlineRoot.DirectoryName);
        var path = System.IO.Path.Combine(root, publication.Authority.Leaf);
        var manifest = NativeTextIncrementalMetadata.ReadManifest(path, runtime.Options.NativeText.Value.MaximumDiskBytes, budget);
        var owner = NativeTextOwnerFiles.ReadOwner(System.IO.Path.Combine(path, NativeTextProtocol.OwnerFile),
            root, publication.Authority.Leaf, request.NodeId, runtime.Options.NativeText);
        RequireLiveInventory(path, owner.OwnedPaths, manifest, runtime, budget);
        return new(path, manifest, owner.OwnedPaths);
    }

    internal async Task RequireRetirementAsync(Task originalRetirement, NativeTextCapturedFileRoster successor,
        TestDatabase fixture, NativeTextOnlineTestRuntime runtime, OnlineTextIndexMaintenanceRequest request,
        CancellationToken token)
    {
        await Assert.That(successor.Path).IsNotEqualTo(Path);
        await originalRetirement.WaitAsync(token);
        await Assert.That(Directory.Exists(Path)).IsFalse();
        successor.RequireCurrent(fixture, runtime, request, token);
    }

    internal void RequireCurrent(TestDatabase fixture, NativeTextOnlineTestRuntime runtime,
        OnlineTextIndexMaintenanceRequest request, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(runtime.Options.Core.DatabaseLimits, fixture.Database.EvaluationClock, token);
        var publication = fixture.Store.Read(raw =>
        {
            var view = budget.CreateView(raw);
            var principal = fixture.Database.Principal(view, NativeTextMaintenanceTestValues.Principal,
                fixture.Database.EvaluationClock.GetUtcNow());
            return fixture.Database.ReadOnlineTextCurrentPublication(view, principal, request, budget)
                ?? throw new InvalidOperationException("The actual current canonical online publication is absent.");
        });
        var root = System.IO.Path.Combine(fixture.Directory, NativeTextOnlineRoot.DirectoryName);
        if (publication.CommandId != request.CommandId || System.IO.Path.Combine(root, publication.Authority.Leaf) != Path)
        { throw new InvalidOperationException("The actual successor canonical online publication changed."); }
        RequireOriginal(fixture, runtime, token);
    }

    internal void RequireOriginal(TestDatabase fixture, NativeTextOnlineTestRuntime runtime, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(runtime.Options.Core.DatabaseLimits, fixture.Database.EvaluationClock, token);
        var actual = NativeTextIncrementalMetadata.ReadManifest(Path, runtime.Options.NativeText.Value.MaximumDiskBytes, budget);
        if (!NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(Manifest)))
        { throw new InvalidOperationException("The original retained native reader manifest changed."); }
        RequireLiveInventory(Path, OwnedPaths, Manifest, runtime, budget);
    }

    private static void RequireLiveInventory(string generationPath, NativeTextOwnedPath[] ownedPaths,
        NativeTextIncrementalManifest manifest, NativeTextOnlineTestRuntime runtime, ReadExecutionBudget budget)
    {
        NativeTextInventory.ValidateTrackedLayout(generationPath, ownedPaths, runtime.Options.NativeText, budget);
        var nativePath = System.IO.Path.Combine(generationPath, NativeTextProtocol.NativeDirectory);
        var actual = Directory.EnumerateFiles(nativePath, "*", SearchOption.AllDirectories)
            .Select(path => NativeTextPath.RelativePath(generationPath, path)).Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(manifest.Files.Select(file => file.RelativePath)))
        { throw new InvalidOperationException("The complete retained native file roster changed."); }
        long bytes = 0;
        foreach (var file in manifest.Files)
        {
            budget.Check();
            var path = System.IO.Path.Combine(generationPath,
                file.RelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            NativeTextFileIO.VerifyRegularFile(path);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                runtime.Options.NativeText.Value.HashBufferBytes, FileOptions.SequentialScan);
            var length = input.Length;
            var digest = NativeTextDigest.HashBounded(input, length,
                runtime.Options.NativeText.Value.MaximumDiskBytes - bytes, budget,
                executionOptions: runtime.Options.NativeText);
            if (input.Length != length || length != file.Length || !digest.AsSpan().SequenceEqual(file.Sha256))
            { throw new InvalidOperationException("The complete retained native file bytes changed."); }
            bytes += length;
        }
    }
}
