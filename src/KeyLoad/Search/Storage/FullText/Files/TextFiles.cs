using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class TextFiles
{
    internal static string InitializeRoot(string root, Guid sourceNodeId, DatabaseLimits limits, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
    {
        RuntimeTextIndex.ValidatePath(root);
        ArgumentOutOfRangeException.ThrowIfEqual(sourceNodeId, Guid.Empty);
        RuntimeTextPath.VerifyExistingAncestors(root);
        Directory.CreateDirectory(root);
        TextFileIO.VerifyDirectory(root);
        TextFileIO.SetPrivateDirectoryMode(root);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var initialized = File.Exists(Path.Combine(fullRoot, TextProtocol.RootReceiptFile));
        resources?.RegisterRoot(fullRoot, budget =>
        {
            budget?.Check();
            TextFileIO.VerifyDirectory(fullRoot);
            if (initialized)
            {
                TextRootFiles.VerifyReceipt(fullRoot, sourceNodeId, executionOptions);
            }
        });
        TextRootFiles.InitializeReceipt(fullRoot, sourceNodeId, executionOptions: executionOptions, resources: resources);
        initialized = true;
        TextRootFiles.RetireRestartGenerations(fullRoot, sourceNodeId, limits, executionOptions: executionOptions, resources: resources);
        return fullRoot;
    }

    internal static void WriteOwner(string root, string leaf, Guid sourceNodeId, TextProjectionScope scope, DatabaseLimits limits, IOptions<TextExecutionOptions> executionOptions, TextGenerationSlot? first = null, TextGenerationSlot? second = null, TextGenerationSlot? third = null, TextResourceOwnership? resources = null)
    {
        TextValidation.ValidateScope(scope, sourceNodeId);
        TextGenerationFiles.CreateOwner(root, leaf, sourceNodeId, scope, limits, first, second, third, executionOptions: executionOptions, resources: resources);
    }

    internal static TextOwnerReceipt ReadOwnerForProvider(string root, string leaf, Guid sourceNodeId, IOptions<TextExecutionOptions> executionOptions)
        => TextOwnerFiles.ReadOwner(Path.Combine(root, leaf, TextProtocol.OwnerFile),
            root, leaf, sourceNodeId, executionOptions: executionOptions);

    internal static void RuntimeTrackPath(string root, string leaf, Guid sourceNodeId, string path, bool directory, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
        => TextOwnerFiles.TrackPath(root, leaf, sourceNodeId, path, directory, executionOptions: executionOptions, resources: resources);

    internal static void RuntimeRequirePath(string root, string leaf, Guid sourceNodeId, string path, bool directory, IOptions<TextExecutionOptions> executionOptions)
        => TextOwnerFiles.RequirePath(root, leaf, sourceNodeId, path, directory, executionOptions: executionOptions);

    internal static void RuntimeValidateTrackedLayout(string generationPath, TextOwnerReceipt owner, IOptions<TextExecutionOptions> executionOptions)
        => TextInventory.ValidateTrackedLayout(generationPath, owner.OwnedPaths, executionOptions: executionOptions);

    internal static TextManifest WritePendingManifest(string root, string leaf, TextProjectionScope scope, TextRecord[] records, TextFile[] files, DatabaseLimits limits, IOptions<TextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null, TextResourceOwnership? resources = null)
    {
        budget?.Check();
        var manifest = new TextManifest(TextProtocol.FormatVersion, scope,
            TextProjectionProtocol.TokenizerVersion, TextProjectionProtocol.HashVersion, records, files);
        TextValidation.ValidateManifest(manifest, scope, limits.MaxScanRecords, executionOptions: executionOptions);
        var encoded = TextEnvelopeCodec.Encode(manifest);
        if (encoded.LongLength > limits.MaxQueryReadBytes)
        {
            throw TextErrors.BoundExceeded();
        }
        var path = Path.Combine(root, leaf);
        var current = TextFileIO.MeasureRegularFiles(path, executionOptions.Value.MaximumFiles,
            executionOptions.Value.MaximumDiskBytes, budget: budget, executionOptions: executionOptions);
        if (current.Files == executionOptions.Value.MaximumFiles
            || encoded.LongLength > executionOptions.Value.MaximumDiskBytes - current.Bytes)
        {
            throw TextErrors.BoundExceeded();
        }
        budget?.Check();
        TextFileIO.WriteEncodedEnvelope(Path.Combine(path, TextProtocol.PendingManifestFile), encoded,
            limits.MaxQueryReadBytes, executionOptions: executionOptions, resources: resources);
        return manifest;
    }

    internal static void PublishManifest(string root, string leaf, TextResourceOwnership? resources = null)
    {
        void Publish() => File.Move(Path.Combine(root, leaf, TextProtocol.PendingManifestFile),
            Path.Combine(root, leaf, TextProtocol.ManifestFile), true);
        if (resources is null)
        {
            Publish();
        }
        else
        {
            resources.MutatePhysical(Publish);
        }
    }

    internal static TextManifest ReadManifest(string path, TextProjectionScope scope, DatabaseLimits limits, IOptions<TextExecutionOptions> executionOptions)
        => TextGenerationFiles.ReadManifest(path, scope, limits, executionOptions: executionOptions);

    internal static TextManifest ValidatePublishedGeneration(string root, string leaf, Guid sourceNodeId, TextProjectionScope scope, DatabaseLimits limits, ReadExecutionBudget budget, IOptions<TextExecutionOptions> executionOptions)
        => TextGenerationFiles.ValidatePublished(Path.Combine(root, leaf), root, leaf, sourceNodeId,
            scope, limits, budget, executionOptions: executionOptions);

    internal static void DeleteOwnedGeneration(string root, string leaf, Guid sourceNodeId, DatabaseLimits limits, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
        => TextGenerationFiles.DeleteOwned(root, leaf, sourceNodeId, limits, executionOptions: executionOptions, resources: resources);

    internal static void DeleteBuildingGeneration(string root, string leaf, Guid sourceNodeId, DatabaseLimits limits, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
        => TextGenerationFiles.DeleteBuilding(root, leaf, sourceNodeId, limits, executionOptions: executionOptions, resources: resources);

    internal static void CheckGenerationBound(string path, IOptions<TextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null)
    {
        budget?.Check();
        _ = TextFileIO.MeasureRegularFiles(path, executionOptions.Value.MaximumFiles,
            executionOptions.Value.MaximumDiskBytes, budget: budget, executionOptions: executionOptions);
    }
}
