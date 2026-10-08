using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalRoot
{
    private const int InitialGenerationCount = 0;
    private const int EmptyAttributes = 0;

    internal static string Initialize(string directory, Guid sourceNodeId,
        IOptions<NativeTextExecutionOptions> executionOptions)
    {
        NativeTextIndex.ValidatePath(directory);
        ArgumentOutOfRangeException.ThrowIfEqual(sourceNodeId, Guid.Empty);
        NativeTextPath.VerifyExistingAncestors(directory);
        Directory.CreateDirectory(directory);
        NativeTextFileIO.VerifyDirectory(directory);
        NativeTextFileIO.SetPrivateDirectoryMode(directory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        NativeTextRootFiles.InitializeReceipt(root, sourceNodeId, executionOptions);
        CheckRoot(root, sourceNodeId, executionOptions);
        return root;
    }

    internal static void CreateGeneration(string root, string leaf, TextProjectionScope scope,
        TextIndexMaintenanceRequest request, long sourceUpperSequence, string resourceSha256,
        ReadExecutionBudget budget,
        IOptions<NativeTextExecutionOptions> executionOptions)
    {
        NativeTextValidation.ValidateScope(scope, scope.NodeId);
        if (sourceUpperSequence < NativeTextIncrementalProtocol.InitialSequence
            || !NativeTextIncrementalDigest.IsCanonical(resourceSha256))
        { throw NativeTextErrors.Corrupt(); }
        if (!NativeTextValidation.IsGenerationLeaf(leaf))
        { throw NativeTextErrors.Ownership(); }
        var generations = CheckRoot(root, scope.NodeId, executionOptions, budget);
        if (generations >= executionOptions.Value.MaximumGenerations)
        { throw NativeTextErrors.BoundExceeded(); }
        if (NativeTextIncrementalEnrollmentFiles.Find(root, request, budget, executionOptions) is not null)
        { throw NativeTextErrors.Mismatch(); }
        var path = Path.Combine(root, leaf);
        if (Directory.Exists(path) || File.Exists(path))
        { throw NativeTextErrors.Ownership(); }
        Directory.CreateDirectory(path);
        NativeTextFileIO.VerifyDirectory(path);
        NativeTextFileIO.SetPrivateDirectoryMode(path);
        NativeTextFileIO.WriteEnvelope(Path.Combine(path, NativeTextProtocol.OwnerFile),
            new NativeTextOwnerReceipt(NativeTextProtocol.FormatVersion, root, leaf, scope.NodeId,
                scope, [new(NativeTextProtocol.NativeDirectory, true)]),
            executionOptions.Value.MaximumOwnerReceiptBytes, executionOptions);
        budget.ChargeBytes(NativeSerialization.Measure(request));
        budget.ChargeBytes(SHA256.HashSizeInBytes);
        var originalDigest = SHA256.HashData(NativeSerialization.Serialize(request));
        var enrollment = new NativeTextIncrementalEnrollment(NativeTextIncrementalProtocol.FormatVersion,
            request.CommandId, scope, request.Consumer, request.IndexGeneration, request.Placement, originalDigest,
            sourceUpperSequence, resourceSha256);
        budget.ChargeBytes(NativeSerialization.Measure(enrollment));
        NativeTextFileIO.WriteEnvelope(Path.Combine(path, NativeTextIncrementalProtocol.EnrollmentFile),
            enrollment, executionOptions.Value.MaximumOwnerReceiptBytes, executionOptions);
        _ = CheckRoot(root, scope.NodeId, executionOptions, budget);
    }

    internal static int CheckRoot(string root, Guid sourceNodeId,
        IOptions<NativeTextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null)
    {
        budget?.Check();
        budget?.ChargeBytes(new FileInfo(Path.Combine(root, NativeTextProtocol.RootReceiptFile)).Length);
        NativeTextRootFiles.VerifyReceipt(root, sourceNodeId, executionOptions);
        var generations = InitialGenerationCount;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (Path.GetFileName(entry) == NativeTextProtocol.RootReceiptFile)
            { continue; }
            var leaf = Path.GetFileName(entry);
            if (!NativeTextValidation.IsGenerationLeaf(leaf) || !Directory.Exists(entry))
            { throw NativeTextErrors.Ownership(); }
            if (++generations > executionOptions.Value.MaximumGenerations)
            { throw NativeTextErrors.BoundExceeded(); }
            budget?.Check();
            NativeTextFileIO.VerifyDirectory(entry);
            budget?.ChargeBytes(new FileInfo(Path.Combine(entry, NativeTextProtocol.OwnerFile)).Length);
            var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(entry, NativeTextProtocol.OwnerFile),
                root, leaf, sourceNodeId, executionOptions);
            CheckEntries(entry);
            var enrollment = NativeTextIncrementalEnrollmentFiles.Read(entry, sourceNodeId, budget, executionOptions);
            if (enrollment.Scope != owner.Scope)
            { throw NativeTextErrors.Corrupt(); }
            if (File.Exists(Path.Combine(entry, NativeTextProtocol.OwnerPendingFile)))
            { throw NativeTextErrors.Corrupt(); }
            NativeTextOwnedInventory.ValidateTrackedLayout(entry, owner.OwnedPaths, budget,
                allowMissingNative: true, executionOptions: executionOptions);
        }
        _ = NativeTextFileIO.MeasureRegularFiles(root, executionOptions.Value.MaximumFiles,
            executionOptions.Value.MaximumDiskBytes, executionOptions, budget);
        return generations;
    }

    private static void CheckEntries(string generationPath)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(generationPath))
        {
            var name = Path.GetFileName(entry);
            if (name is not (NativeTextProtocol.OwnerFile or NativeTextProtocol.OwnerPendingFile
                or NativeTextProtocol.NativeDirectory or NativeTextIncrementalProtocol.EnrollmentFile
                or NativeTextIncrementalProtocol.ManifestFile
                or NativeTextIncrementalProtocol.PendingManifestFile or NativeTextIncrementalProtocol.IntentFile
                or NativeTextIncrementalProtocol.PendingIntentFile))
            { throw NativeTextErrors.Ownership(); }
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributes
                || (name == NativeTextProtocol.NativeDirectory) != ((attributes & FileAttributes.Directory) != EmptyAttributes))
            { throw NativeTextErrors.Ownership(); }
        }
    }
}
