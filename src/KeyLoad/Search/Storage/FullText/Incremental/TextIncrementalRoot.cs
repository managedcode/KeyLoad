using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class TextIncrementalRoot
{
    private const int InitialGenerationCount = 0;
    private const int EmptyAttributes = 0;

    internal static string Initialize(string directory, Guid sourceNodeId,
        IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
    {
        RuntimeTextIndex.ValidatePath(directory);
        ArgumentOutOfRangeException.ThrowIfEqual(sourceNodeId, Guid.Empty);
        RuntimeTextPath.VerifyExistingAncestors(directory);
        Directory.CreateDirectory(directory);
        TextFileIO.VerifyDirectory(directory);
        TextFileIO.SetPrivateDirectoryMode(directory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var initialized = File.Exists(Path.Combine(root, TextProtocol.RootReceiptFile));
        resources?.RegisterRoot(root, budget =>
        {
            budget?.Check();
            TextFileIO.VerifyDirectory(root);
            if (initialized)
            {
                TextRootFiles.VerifyReceipt(root, sourceNodeId, executionOptions);
            }
        });
        TextRootFiles.InitializeReceipt(root, sourceNodeId, executionOptions, resources);
        initialized = true;
        CheckRoot(root, sourceNodeId, executionOptions);
        return root;
    }

    internal static void CreateGeneration(string root, string leaf, TextProjectionScope scope,
        TextIndexMaintenanceRequest request, long sourceUpperSequence, string resourceSha256,
        ReadExecutionBudget budget,
        IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
    {
        TextValidation.ValidateScope(scope, scope.NodeId);
        if (sourceUpperSequence < TextIncrementalProtocol.InitialSequence
            || !TextIncrementalDigest.IsCanonical(resourceSha256))
        {
            throw TextErrors.Corrupt();
        }
        if (!TextValidation.IsGenerationLeaf(leaf))
        {
            throw TextErrors.Ownership();
        }
        var generations = CheckRoot(root, scope.NodeId, executionOptions, budget);
        if (generations >= executionOptions.Value.MaximumGenerations)
        {
            throw TextErrors.BoundExceeded();
        }
        if (TextIncrementalEnrollmentFiles.Find(root, request, budget, executionOptions) is not null)
        {
            throw TextErrors.Mismatch();
        }
        var path = Path.Combine(root, leaf);
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw TextErrors.Ownership();
        }
        if (resources is null)
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            resources.MutatePhysical(() => Directory.CreateDirectory(path), budget);
        }
        TextFileIO.VerifyDirectory(path);
        TextFileIO.SetPrivateDirectoryMode(path);
        TextFileIO.WriteEnvelope(Path.Combine(path, TextProtocol.OwnerFile),
            new TextOwnerReceipt(TextProtocol.FormatVersion, root, leaf, scope.NodeId,
                scope, [new(TextProtocol.RuntimeDirectoryCore, true)]),
            executionOptions.Value.MaximumOwnerReceiptBytes, executionOptions, resources);
        budget.ChargeBytes(OrleansSerialization.Measure(request));
        budget.ChargeBytes(SHA256.HashSizeInBytes);
        var originalDigest = SHA256.HashData(OrleansSerialization.Serialize(request));
        var enrollment = new TextIncrementalEnrollment(TextIncrementalProtocol.FormatVersion,
            request.CommandId, scope, request.Consumer, request.IndexGeneration, request.Placement, originalDigest,
            sourceUpperSequence, resourceSha256);
        budget.ChargeBytes(OrleansSerialization.Measure(enrollment));
        TextFileIO.WriteEnvelope(Path.Combine(path, TextIncrementalProtocol.EnrollmentFile),
            enrollment, executionOptions.Value.MaximumOwnerReceiptBytes, executionOptions, resources);
        _ = CheckRoot(root, scope.NodeId, executionOptions, budget);
    }

    internal static int CheckRoot(string root, Guid sourceNodeId,
        IOptions<TextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null)
    {
        budget?.Check();
        budget?.ChargeBytes(new FileInfo(Path.Combine(root, TextProtocol.RootReceiptFile)).Length);
        TextRootFiles.VerifyReceipt(root, sourceNodeId, executionOptions);
        var generations = InitialGenerationCount;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (Path.GetFileName(entry) == TextProtocol.RootReceiptFile)
            {
                continue;
            }
            var leaf = Path.GetFileName(entry);
            if (!TextValidation.IsGenerationLeaf(leaf) || !Directory.Exists(entry))
            {
                throw TextErrors.Ownership();
            }
            if (++generations > executionOptions.Value.MaximumGenerations)
            {
                throw TextErrors.BoundExceeded();
            }
            budget?.Check();
            TextFileIO.VerifyDirectory(entry);
            budget?.ChargeBytes(new FileInfo(Path.Combine(entry, TextProtocol.OwnerFile)).Length);
            var owner = TextOwnerFiles.ReadOwner(Path.Combine(entry, TextProtocol.OwnerFile),
                root, leaf, sourceNodeId, executionOptions);
            CheckEntries(entry);
            var enrollment = TextIncrementalEnrollmentFiles.Read(entry, sourceNodeId, budget, executionOptions);
            if (enrollment.Scope != owner.Scope)
            {
                throw TextErrors.Corrupt();
            }
            if (File.Exists(Path.Combine(entry, TextProtocol.OwnerPendingFile)))
            {
                throw TextErrors.Corrupt();
            }
            TextOwnedInventory.ValidateTrackedLayout(entry, owner.OwnedPaths, budget,
                runtimeAllowMissing: true, executionOptions: executionOptions);
        }
        _ = TextFileIO.MeasureRegularFiles(root, executionOptions.Value.MaximumFiles,
            executionOptions.Value.MaximumDiskBytes, executionOptions, budget);
        return generations;
    }

    private static void CheckEntries(string generationPath)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(generationPath))
        {
            var name = Path.GetFileName(entry);
            if (name is not (TextProtocol.OwnerFile or TextProtocol.OwnerPendingFile
                or TextProtocol.RuntimeDirectoryCore or TextIncrementalProtocol.EnrollmentFile
                or TextIncrementalProtocol.ManifestFile
                or TextIncrementalProtocol.PendingManifestFile or TextIncrementalProtocol.IntentFile
                or TextIncrementalProtocol.PendingIntentFile))
            {
                throw TextErrors.Ownership();
            }
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributes
                || (name == TextProtocol.RuntimeDirectoryCore) != ((attributes & FileAttributes.Directory) != EmptyAttributes))
            {
                throw TextErrors.Ownership();
            }
        }
    }
}
