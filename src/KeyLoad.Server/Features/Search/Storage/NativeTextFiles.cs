using Microsoft.Extensions.Options;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextFiles
{
    internal static string InitializeRoot(string root, Guid sourceNodeId, DatabaseLimits limits, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        NativeTextIndex.ValidatePath(root);
        ArgumentOutOfRangeException.ThrowIfEqual(sourceNodeId, Guid.Empty);
        NativeTextPath.VerifyExistingAncestors(root);
        Directory.CreateDirectory(root);
        NativeTextFileIO.VerifyDirectory(root);
        NativeTextFileIO.SetPrivateDirectoryMode(root);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        NativeTextRootFiles.InitializeReceipt(fullRoot, sourceNodeId, executionOptions: executionOptions);
        NativeTextRootFiles.RetireRestartGenerations(fullRoot, sourceNodeId, limits, executionOptions: executionOptions);
        return fullRoot;
    }

    internal static void WriteOwner(string root, string leaf, Guid sourceNodeId, TextProjectionScope scope, DatabaseLimits limits, IOptions<NativeTextExecutionOptions> executionOptions, NativeTextGenerationSlot? first = null, NativeTextGenerationSlot? second = null, NativeTextGenerationSlot? third = null)
    {
        NativeTextValidation.ValidateScope(scope, sourceNodeId);
        NativeTextGenerationFiles.CreateOwner(root, leaf, sourceNodeId, scope, limits, first, second, third, executionOptions: executionOptions);
    }

    internal static NativeTextOwnerReceipt ReadOwnerForProvider(string root, string leaf, Guid sourceNodeId, IOptions<NativeTextExecutionOptions> executionOptions)
        => NativeTextOwnerFiles.ReadOwner(Path.Combine(root, leaf, NativeTextProtocol.OwnerFile),
            root, leaf, sourceNodeId, executionOptions: executionOptions);

    internal static void TrackNativePath(string root, string leaf, Guid sourceNodeId, string path, bool directory, IOptions<NativeTextExecutionOptions> executionOptions)
        => NativeTextOwnerFiles.TrackPath(root, leaf, sourceNodeId, path, directory, executionOptions: executionOptions);

    internal static void RequireNativePath(string root, string leaf, Guid sourceNodeId, string path, bool directory, IOptions<NativeTextExecutionOptions> executionOptions)
        => NativeTextOwnerFiles.RequirePath(root, leaf, sourceNodeId, path, directory, executionOptions: executionOptions);

    internal static void ValidateTrackedNativeLayout(string generationPath, NativeTextOwnerReceipt owner, IOptions<NativeTextExecutionOptions> executionOptions)
        => NativeTextInventory.ValidateTrackedLayout(generationPath, owner.OwnedPaths, executionOptions: executionOptions);

    internal static NativeTextManifest WritePendingManifest(string root, string leaf, TextProjectionScope scope, NativeTextRecord[] records, NativeTextFile[] files, DatabaseLimits limits, IOptions<NativeTextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null)
    {
        budget?.Check();
        var manifest = new NativeTextManifest(NativeTextProtocol.FormatVersion, scope,
            TextProjectionProtocol.TokenizerVersion, TextProjectionProtocol.HashVersion, records, files);
        NativeTextValidation.ValidateManifest(manifest, scope, limits.MaxScanRecords, executionOptions: executionOptions);
        var encoded = NativeTextEnvelopeCodec.Encode(manifest);
        if (encoded.LongLength > limits.MaxQueryReadBytes)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        var path = Path.Combine(root, leaf);
        var current = NativeTextFileIO.MeasureRegularFiles(path, executionOptions.Value.MaximumFiles,
            executionOptions.Value.MaximumDiskBytes, budget: budget, executionOptions: executionOptions);
        if (current.Files == executionOptions.Value.MaximumFiles
            || encoded.LongLength > executionOptions.Value.MaximumDiskBytes - current.Bytes)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        budget?.Check();
        NativeTextFileIO.WriteEncodedEnvelope(Path.Combine(path, NativeTextProtocol.PendingManifestFile), encoded,
            limits.MaxQueryReadBytes, executionOptions: executionOptions);
        return manifest;
    }

    internal static void PublishManifest(string root, string leaf)
        => File.Move(Path.Combine(root, leaf, NativeTextProtocol.PendingManifestFile),
            Path.Combine(root, leaf, NativeTextProtocol.ManifestFile), true);

    internal static NativeTextManifest ReadManifest(string path, TextProjectionScope scope, DatabaseLimits limits, IOptions<NativeTextExecutionOptions> executionOptions)
        => NativeTextGenerationFiles.ReadManifest(path, scope, limits, executionOptions: executionOptions);

    internal static NativeTextManifest ValidatePublishedGeneration(string root, string leaf, Guid sourceNodeId, TextProjectionScope scope, DatabaseLimits limits, ReadExecutionBudget budget, IOptions<NativeTextExecutionOptions> executionOptions)
        => NativeTextGenerationFiles.ValidatePublished(Path.Combine(root, leaf), root, leaf, sourceNodeId,
            scope, limits, budget, executionOptions: executionOptions);

    internal static void DeleteOwnedGeneration(string root, string leaf, Guid sourceNodeId, DatabaseLimits limits, IOptions<NativeTextExecutionOptions> executionOptions)
        => NativeTextGenerationFiles.DeleteOwned(root, leaf, sourceNodeId, limits, executionOptions: executionOptions);

    internal static void DeleteBuildingGeneration(string root, string leaf, Guid sourceNodeId, DatabaseLimits limits, IOptions<NativeTextExecutionOptions> executionOptions)
        => NativeTextGenerationFiles.DeleteBuilding(root, leaf, sourceNodeId, limits, executionOptions: executionOptions);

    internal static void CheckGenerationBound(string path, IOptions<NativeTextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null)
    {
        budget?.Check();
        _ = NativeTextFileIO.MeasureRegularFiles(path, executionOptions.Value.MaximumFiles,
            executionOptions.Value.MaximumDiskBytes, budget: budget, executionOptions: executionOptions);
    }
}
