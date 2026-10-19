using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class TextOnlineCatalogFiles
{
    private const long UnissuedConsumerGeneration = 0;
    private const long InitialAppliedPosition = 0;
    private const long InitialThroughSequence = 0;
    internal const string CurrentFile = "catalog.bin";
    internal const string PendingFile = "catalog.pending";

    internal static TextOnlineCatalog Read(string root, ReadExecutionBudget budget,
        IOptions<TextExecutionOptions> options)
    {
        budget.Check();
        TextFileIO.VerifyDirectory(root);
        var path = Path.Combine(root, CurrentFile);
        TextFileIO.VerifyRegularFile(path);
        budget.ChargeBytes(new FileInfo(path).Length);
        var result = TextFileIO.ReadEnvelope<TextOnlineCatalog>(path,
            options.Value.MaximumOwnerReceiptBytes);
        ValidateShape(result);
        budget.Check();
        return result;
    }

    internal static void Publish(string root, TextOnlineCatalogProof proof,
        DatabaseEngine database, ReadExecutionBudget budget,
        IOptions<TextExecutionOptions> options, TextResourceOwnership resources,
        Action<TextFaultStage>? observer = null)
    {
        budget.Check();
        var catalog = proof.Catalog;
        ValidateShape(catalog);
        proof.RequireExact(catalog, budget);
        proof.RequireCurrent(database, budget);
        TextFileIO.VerifyDirectory(root);
        var current = Path.Combine(root, CurrentFile);
        var pending = Path.Combine(root, PendingFile);
        if (File.Exists(pending))
        {
            TextFileIO.VerifyRegularFile(pending);
            budget.ChargeBytes(new FileInfo(pending).Length);
            var retainedPending = TextFileIO.ReadEnvelope<TextOnlineCatalog>(pending,
                options.Value.MaximumOwnerReceiptBytes);
            RequireRebind(retainedPending, catalog, budget);
            // A fresh complete authority/inventory/corpus proof authorizes discarding this exact disposable pending image.
            proof.RequireCurrent(database, budget);
            resources.DeleteOwnedFile(pending, () => File.Delete(pending));
        }
        if (File.Exists(current))
        {
            TextFileIO.VerifyRegularFile(current);
        }
        budget.ChargeBytes(OrleansSerialization.Measure(catalog));
        TextFileIO.WriteEnvelope(pending, catalog, options.Value.MaximumOwnerReceiptBytes, options, resources);
        budget.ChargeBytes(new FileInfo(pending).Length);
        var verified = TextFileIO.ReadEnvelope<TextOnlineCatalog>(pending,
            options.Value.MaximumOwnerReceiptBytes);
        ValidateShape(verified);
        proof.RequireExact(verified, budget);
        proof.RequireCurrent(database, budget);
        budget.Check();
        observer?.Invoke(TextFaultStage.OnlineCatalogPendingFlushed);
        resources.MutatePhysical(() => File.Move(pending, current, overwrite: true), budget);
        budget.Check();
    }

    internal static void RequireCurrentCatalog(string root, TextOnlineCatalog expected,
        ReadExecutionBudget budget, IOptions<TextExecutionOptions> options)
    {
        if (File.Exists(Path.Combine(root, PendingFile)))
        {
            throw TextErrors.Ownership();
        }
        RequireRebind(Read(root, budget, options), expected, budget);
    }

    private static void RequireRebind(TextOnlineCatalog original, TextOnlineCatalog fresh,
        ReadExecutionBudget budget)
    {
        ValidateShape(original);
        ValidateShape(fresh);
        if (original.Scope.ReadGeneration > fresh.Scope.ReadGeneration || original.Scope.Position > fresh.Scope.Position
            || original.AppliedPosition > fresh.AppliedPosition || original.ThroughSequence > fresh.ThroughSequence)
        {
            throw TextErrors.Mismatch();
        }
        var normalized = original with
        {
            Scope = original.Scope with
            {
                ReadGeneration = fresh.Scope.ReadGeneration,
                Position = fresh.Scope.Position
            },
            AppliedPosition = fresh.AppliedPosition,
            ThroughSequence = fresh.ThroughSequence
        };
        budget.ChargeBytes(OrleansSerialization.Measure(normalized));
        budget.ChargeBytes(OrleansSerialization.Measure(fresh));
        if (!OrleansSerialization.Serialize(normalized).AsSpan().SequenceEqual(OrleansSerialization.Serialize(fresh)))
        {
            throw TextErrors.Mismatch();
        }
        budget.Check();
    }

    private static void ValidateShape(TextOnlineCatalog catalog)
    {
        if (catalog.FormatVersion != TextProtocol.FormatVersion || catalog.BuildCommandId == Guid.Empty
            || catalog.ConsumerGeneration <= UnissuedConsumerGeneration || catalog.AppliedPosition < InitialAppliedPosition || catalog.ThroughSequence < InitialThroughSequence
            || catalog.NodeId != catalog.Scope.NodeId || catalog.Consumer.Partition != catalog.Scope.Partition
            || !TextValidation.IsGenerationLeaf(catalog.Leaf)
            || !TextIncrementalDigest.IsCanonical(catalog.ManifestSha256))
        {
            throw TextErrors.Corrupt();
        }
        TextValidation.ValidateScope(catalog.Scope, catalog.NodeId);
    }
}
