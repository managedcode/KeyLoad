using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextFiles
{
    internal static string InitializeRoot(string root, Guid sourceNodeId, DatabaseLimits limits)
    {
        NativeTextIndex.ValidatePath(root);
        ArgumentOutOfRangeException.ThrowIfEqual(sourceNodeId, Guid.Empty);
        NativeTextPath.VerifyExistingAncestors(root);
        Directory.CreateDirectory(root);
        NativeTextFileIO.VerifyDirectory(root);
        NativeTextFileIO.SetPrivateDirectoryMode(root);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        NativeTextRootFiles.InitializeReceipt(fullRoot, sourceNodeId);
        NativeTextRootFiles.RetireRestartGenerations(fullRoot, sourceNodeId, limits);
        return fullRoot;
    }

    internal static void WriteOwner(string root, string leaf, Guid sourceNodeId, TextProjectionScope scope,
        DatabaseLimits limits, NativeTextGenerationSlot? first = null, NativeTextGenerationSlot? second = null,
        NativeTextGenerationSlot? third = null)
    {
        NativeTextValidation.ValidateScope(scope, sourceNodeId);
        NativeTextGenerationFiles.CreateOwner(root, leaf, sourceNodeId, scope, limits, first, second, third);
    }

    internal static NativeTextOwnerReceipt ReadOwnerForProvider(string root, string leaf, Guid sourceNodeId)
        => NativeTextOwnerFiles.ReadOwner(Path.Combine(root, leaf, NativeTextProtocol.OwnerFile),
            root, leaf, sourceNodeId);

    internal static void TrackNativePath(string root, string leaf, Guid sourceNodeId, string path, bool directory)
        => NativeTextOwnerFiles.TrackPath(root, leaf, sourceNodeId, path, directory);

    internal static void RequireNativePath(string root, string leaf, Guid sourceNodeId, string path, bool directory)
        => NativeTextOwnerFiles.RequirePath(root, leaf, sourceNodeId, path, directory);

    internal static void ValidateTrackedNativeLayout(string generationPath, NativeTextOwnerReceipt owner)
        => NativeTextInventory.ValidateTrackedLayout(generationPath, owner.OwnedPaths);

    internal static NativeTextManifest WritePendingManifest(string root, string leaf, TextProjectionScope scope,
        NativeTextRecord[] records, NativeTextFile[] files, DatabaseLimits limits, ReadExecutionBudget? budget = null)
    {
        budget?.Check();
        var manifest = new NativeTextManifest(NativeTextProtocol.FormatVersion, scope,
            TextProjectionProtocol.TokenizerVersion, TextProjectionProtocol.HashVersion, records, files);
        NativeTextValidation.ValidateManifest(manifest, scope, limits.MaxScanRecords);
        var encoded = NativeTextEnvelopeCodec.Encode(manifest);
        if (encoded.LongLength > limits.MaxQueryReadBytes)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        var path = Path.Combine(root, leaf);
        var current = NativeTextFileIO.MeasureRegularFiles(path, NativeTextProtocol.MaximumFiles,
            NativeTextProtocol.MaximumDiskBytes, budget);
        if (current.Files == NativeTextProtocol.MaximumFiles
            || encoded.LongLength > NativeTextProtocol.MaximumDiskBytes - current.Bytes)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        budget?.Check();
        NativeTextFileIO.WriteEncodedEnvelope(Path.Combine(path, NativeTextProtocol.PendingManifestFile), encoded,
            limits.MaxQueryReadBytes);
        return manifest;
    }

    internal static void PublishManifest(string root, string leaf)
        => File.Move(Path.Combine(root, leaf, NativeTextProtocol.PendingManifestFile),
            Path.Combine(root, leaf, NativeTextProtocol.ManifestFile), true);

    internal static NativeTextManifest ReadManifest(string path, TextProjectionScope scope, DatabaseLimits limits)
        => NativeTextGenerationFiles.ReadManifest(path, scope, limits);

    internal static NativeTextManifest ValidatePublishedGeneration(string root, string leaf, Guid sourceNodeId,
        TextProjectionScope scope, DatabaseLimits limits, ReadExecutionBudget budget)
        => NativeTextGenerationFiles.ValidatePublished(Path.Combine(root, leaf), root, leaf, sourceNodeId,
            scope, limits, budget);

    internal static void DeleteOwnedGeneration(string root, string leaf, Guid sourceNodeId, DatabaseLimits limits)
        => NativeTextGenerationFiles.DeleteOwned(root, leaf, sourceNodeId, limits);

    internal static void DeleteBuildingGeneration(string root, string leaf, Guid sourceNodeId, DatabaseLimits limits)
        => NativeTextGenerationFiles.DeleteBuilding(root, leaf, sourceNodeId, limits);

    internal static void CheckGenerationBound(string path, ReadExecutionBudget? budget = null)
    {
        budget?.Check();
        _ = NativeTextFileIO.MeasureRegularFiles(path, NativeTextProtocol.MaximumFiles,
            NativeTextProtocol.MaximumDiskBytes, budget);
    }
}
