using KeyLoad.Core;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal sealed class TextOnlineRoot
{
    internal const string DirectoryName = "execution-text-online";
    private readonly Guid node;
    private readonly IOptions<TextExecutionOptions> options;
    private readonly TextResourceOwnership resources;

    internal TextOnlineRoot(string directory, Guid node, IOptions<TextExecutionOptions> options,
        TextResourceOwnership resources)
    {
        this.node = node;
        this.options = options;
        this.resources = resources;
        var root = Path.GetFullPath(Path.Combine(directory, DirectoryName));
        RuntimeTextIndex.ValidatePath(root);
        RuntimeTextPath.VerifyExistingAncestors(root);
        System.IO.Directory.CreateDirectory(root);
        TextFileIO.VerifyDirectory(root);
        TextFileIO.SetPrivateDirectoryMode(root);
        var initialized = false;
        resources.RegisterRoot(root, budget =>
        {
            budget?.Check();
            TextFileIO.VerifyDirectory(root);
            if (initialized)
            {
                TextRootFiles.VerifyReceipt(root, node, options);
            }
        });
        TextRootFiles.InitializeReceipt(root, node, options, resources);
        initialized = true;
        Directory = root;
    }

    internal string Directory
    {
        get;
    }

    internal void CreateGeneration(string leaf, TextProjectionScope scope,
        TextResourceReservation reservation, ReadExecutionBudget budget)
    {
        Verify(budget);
        TextValidation.ValidateScope(scope, node);
        reservation.RequireGeneration(resources, Path.Combine(Directory, leaf));
        var path = Path.Combine(Directory, leaf);
        resources.MutatePhysical(() =>
        {
            System.IO.Directory.CreateDirectory(path);
            TextFileIO.VerifyDirectory(path);
            TextFileIO.SetPrivateDirectoryMode(path);
            var owner = new TextOwnerReceipt(TextProtocol.FormatVersion, Directory, leaf, node,
                scope, [new(TextProtocol.RuntimeDirectoryCore, true)]);
            budget.ChargeBytes(OrleansSerialization.Measure(owner));
            TextFileIO.WriteEnvelope(Path.Combine(path, TextProtocol.OwnerFile), owner,
                options.Value.MaximumOwnerReceiptBytes, options, resources);
        }, budget);
    }

    internal void Verify(ReadExecutionBudget budget)
    {
        budget.Check();
        TextRootFiles.VerifyReceipt(Directory, node, options);
        foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(Directory))
        {
            budget.Check();
            var leaf = Path.GetFileName(entry);
            if (leaf is TextProtocol.RootReceiptFile or TextOnlineCatalogFiles.CurrentFile
                or TextOnlineCatalogFiles.PendingFile)
            {
                TextFileIO.VerifyRegularFile(entry);
                continue;
            }
            if (!TextValidation.IsGenerationLeaf(leaf))
            {
                throw TextErrors.Ownership();
            }
            TextFileIO.VerifyDirectory(entry);
            VerifyGenerationEntries(entry, leaf, budget);
            var owner = TextOwnerFiles.ReadOwner(Path.Combine(entry, TextProtocol.OwnerFile),
                Directory, leaf, node, options);
            TextOwnedInventory.ValidateTrackedLayout(entry, owner.OwnedPaths, budget,
                runtimeAllowMissing: true, executionOptions: options);
        }
    }

    private void VerifyGenerationEntries(string path, string leaf, ReadExecutionBudget? budget)
    {
        TextFileIO.VerifyDirectory(path);
        foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(path))
        {
            budget?.Check();
            var name = Path.GetFileName(entry);
            if (name == TextProtocol.RuntimeDirectoryCore)
            {
                TextFileIO.VerifyDirectory(entry);
                continue;
            }
            if (name is not (TextProtocol.OwnerFile or TextProtocol.OwnerPendingFile
                or TextIncrementalProtocol.ManifestFile or TextIncrementalProtocol.PendingManifestFile
                or TextIncrementalProtocol.IntentFile or TextIncrementalProtocol.PendingIntentFile
                or TextOnlineStagingFiles.FileName))
            {
                throw TextErrors.Ownership();
            }
            TextFileIO.VerifyRegularFile(entry);
        }
        var receipt = Path.Combine(path, TextOnlineStagingFiles.FileName);
        if (!File.Exists(receipt))
        {
            return;
        } // Partial owner creation is retained; never a publication proof.
        budget?.ChargeBytes(new FileInfo(receipt).Length);
        var staging = TextFileIO.ReadEnvelope<TextOnlineStaging>(receipt,
            options.Value.MaximumOwnerReceiptBytes);
        if (staging.FormatVersion != TextProtocol.FormatVersion || staging.NodeId != node
            || staging.Leaf != leaf || staging.Request.NodeId != node || staging.Request.CommandId == Guid.Empty
            || staging.CapturedCut.NodeId != node || !TextIncrementalDigest.IsCanonical(staging.ParentFingerprint))
        {
            throw TextErrors.Corrupt();
        }
        budget?.ChargeBytes(OrleansSerialization.Measure(staging.Request));
        var actual = KeyLoad.Core.Search.OnlineTextParentIdentity.Fingerprint(staging.PrincipalId, staging.Request);
        if (actual != staging.ParentFingerprint)
        {
            throw TextErrors.Mismatch();
        }
    }

    internal void DeleteAfterJoinedOwnership(string leaf, TextProjectionScope scope)
    {
        TextValidation.ValidateScope(scope, node);
        var path = Path.Combine(Directory, leaf);
        if (!TextValidation.IsGenerationLeaf(leaf))
        {
            throw TextErrors.Ownership();
        }
        VerifyGenerationEntries(path, leaf, null);
        var owner = TextOwnerFiles.ReadOwner(Path.Combine(path, TextProtocol.OwnerFile),
            Directory, leaf, node, options);
        if (owner.Scope != scope)
        {
            throw TextErrors.Mismatch();
        }
        TextOwnedInventory.ValidateTrackedLayout(path, owner.OwnedPaths, null,
            runtimeAllowMissing: true, executionOptions: options);
        resources.DeleteOwnedDirectory(path, () => System.IO.Directory.Delete(path, recursive: true));
    }
}
