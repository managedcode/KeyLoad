using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOnlineRoot
{
    internal const string DirectoryName = "native-text-online";
    private readonly Guid node;
    private readonly IOptions<NativeTextExecutionOptions> options;
    private readonly NativeTextResourceOwnership resources;

    internal NativeTextOnlineRoot(string directory, Guid node, IOptions<NativeTextExecutionOptions> options,
        NativeTextResourceOwnership resources)
    {
        this.node = node;
        this.options = options;
        this.resources = resources;
        var root = Path.GetFullPath(Path.Combine(directory, DirectoryName));
        NativeTextIndex.ValidatePath(root);
        NativeTextPath.VerifyExistingAncestors(root);
        System.IO.Directory.CreateDirectory(root);
        NativeTextFileIO.VerifyDirectory(root);
        NativeTextFileIO.SetPrivateDirectoryMode(root);
        var initialized = false;
        resources.RegisterRoot(root, budget =>
        {
            budget?.Check();
            NativeTextFileIO.VerifyDirectory(root);
            if (initialized)
            { NativeTextRootFiles.VerifyReceipt(root, node, options); }
        });
        NativeTextRootFiles.InitializeReceipt(root, node, options, resources);
        initialized = true;
        Directory = root;
    }

    internal string Directory { get; }

    internal void CreateGeneration(string leaf, TextProjectionScope scope,
        NativeTextResourceReservation reservation, ReadExecutionBudget budget)
    {
        Verify(budget);
        NativeTextValidation.ValidateScope(scope, node);
        reservation.RequireGeneration(resources, Path.Combine(Directory, leaf));
        var path = Path.Combine(Directory, leaf);
        resources.MutatePhysical(() =>
        {
            System.IO.Directory.CreateDirectory(path);
            NativeTextFileIO.VerifyDirectory(path);
            NativeTextFileIO.SetPrivateDirectoryMode(path);
            var owner = new NativeTextOwnerReceipt(NativeTextProtocol.FormatVersion, Directory, leaf, node,
                scope, [new(NativeTextProtocol.NativeDirectory, true)]);
            budget.ChargeBytes(NativeSerialization.Measure(owner));
            NativeTextFileIO.WriteEnvelope(Path.Combine(path, NativeTextProtocol.OwnerFile), owner,
                options.Value.MaximumOwnerReceiptBytes, options, resources);
        }, budget);
    }

    internal void Verify(ReadExecutionBudget budget)
    {
        budget.Check();
        NativeTextRootFiles.VerifyReceipt(Directory, node, options);
        foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(Directory))
        {
            budget.Check();
            var leaf = Path.GetFileName(entry);
            if (leaf is NativeTextProtocol.RootReceiptFile or NativeTextOnlineCatalogFiles.CurrentFile
                or NativeTextOnlineCatalogFiles.PendingFile)
            { NativeTextFileIO.VerifyRegularFile(entry); continue; }
            if (!NativeTextValidation.IsGenerationLeaf(leaf))
            { throw NativeTextErrors.Ownership(); }
            NativeTextFileIO.VerifyDirectory(entry);
            VerifyGenerationEntries(entry, leaf, budget);
            var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(entry, NativeTextProtocol.OwnerFile),
                Directory, leaf, node, options);
            NativeTextOwnedInventory.ValidateTrackedLayout(entry, owner.OwnedPaths, budget,
                allowMissingNative: true, executionOptions: options);
        }
    }

    private void VerifyGenerationEntries(string path, string leaf, ReadExecutionBudget? budget)
    {
        NativeTextFileIO.VerifyDirectory(path);
        foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(path))
        {
            budget?.Check();
            var name = Path.GetFileName(entry);
            if (name == NativeTextProtocol.NativeDirectory)
            { NativeTextFileIO.VerifyDirectory(entry); continue; }
            if (name is not (NativeTextProtocol.OwnerFile or NativeTextProtocol.OwnerPendingFile
                or NativeTextIncrementalProtocol.ManifestFile or NativeTextIncrementalProtocol.PendingManifestFile
                or NativeTextIncrementalProtocol.IntentFile or NativeTextIncrementalProtocol.PendingIntentFile
                or NativeTextOnlineStagingFiles.FileName))
            { throw NativeTextErrors.Ownership(); }
            NativeTextFileIO.VerifyRegularFile(entry);
        }
        var receipt = Path.Combine(path, NativeTextOnlineStagingFiles.FileName);
        if (!File.Exists(receipt))
        { return; } // Partial owner creation is retained; never a publication proof.
        budget?.ChargeBytes(new FileInfo(receipt).Length);
        var staging = NativeTextFileIO.ReadEnvelope<NativeTextOnlineStaging>(receipt,
            options.Value.MaximumOwnerReceiptBytes);
        if (staging.FormatVersion != NativeTextProtocol.FormatVersion || staging.NodeId != node
            || staging.Leaf != leaf || staging.Request.NodeId != node || staging.Request.CommandId == Guid.Empty
            || staging.CapturedCut.NodeId != node || !NativeTextIncrementalDigest.IsCanonical(staging.ParentFingerprint))
        { throw NativeTextErrors.Corrupt(); }
        budget?.ChargeBytes(NativeSerialization.Measure(staging.Request));
        var actual = KeyLoad.Core.Features.Search.OnlineTextParentIdentity.Fingerprint(staging.PrincipalId, staging.Request);
        if (actual != staging.ParentFingerprint)
        { throw NativeTextErrors.Mismatch(); }
    }

    internal void DeleteAfterJoinedOwnership(string leaf, TextProjectionScope scope)
    {
        NativeTextValidation.ValidateScope(scope, node);
        var path = Path.Combine(Directory, leaf);
        if (!NativeTextValidation.IsGenerationLeaf(leaf))
        { throw NativeTextErrors.Ownership(); }
        VerifyGenerationEntries(path, leaf, null);
        var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(path, NativeTextProtocol.OwnerFile),
            Directory, leaf, node, options);
        if (owner.Scope != scope)
        { throw NativeTextErrors.Mismatch(); }
        NativeTextOwnedInventory.ValidateTrackedLayout(path, owner.OwnedPaths, null,
            allowMissingNative: true, executionOptions: options);
        resources.DeleteOwnedDirectory(path, () => System.IO.Directory.Delete(path, recursive: true));
    }
}
