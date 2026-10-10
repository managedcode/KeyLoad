using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineCatalogFiles
{
    private const long UnissuedConsumerGeneration = 0;
    private const long InitialAppliedPosition = 0;
    private const long InitialThroughSequence = 0;
    internal const string CurrentFile = "catalog.bin";
    internal const string PendingFile = "catalog.pending";

    internal static NativeTextOnlineCatalog Read(string root, ReadExecutionBudget budget,
        IOptions<NativeTextExecutionOptions> options)
    {
        budget.Check();
        NativeTextFileIO.VerifyDirectory(root);
        var path = Path.Combine(root, CurrentFile);
        NativeTextFileIO.VerifyRegularFile(path);
        budget.ChargeBytes(new FileInfo(path).Length);
        var result = NativeTextFileIO.ReadEnvelope<NativeTextOnlineCatalog>(path,
            options.Value.MaximumOwnerReceiptBytes);
        ValidateShape(result);
        budget.Check();
        return result;
    }

    internal static void Publish(string root, NativeTextOnlineCatalogProof proof,
        DatabaseEngine database, ReadExecutionBudget budget,
        IOptions<NativeTextExecutionOptions> options, NativeTextResourceOwnership resources,
        Action<NativeTextFaultStage>? observer = null)
    {
        budget.Check();
        var catalog = proof.Catalog;
        ValidateShape(catalog);
        proof.RequireExact(catalog, budget);
        proof.RequireCurrent(database, budget);
        NativeTextFileIO.VerifyDirectory(root);
        var current = Path.Combine(root, CurrentFile);
        var pending = Path.Combine(root, PendingFile);
        if (File.Exists(pending))
        {
            NativeTextFileIO.VerifyRegularFile(pending);
            budget.ChargeBytes(new FileInfo(pending).Length);
            var retainedPending = NativeTextFileIO.ReadEnvelope<NativeTextOnlineCatalog>(pending,
                options.Value.MaximumOwnerReceiptBytes);
            RequireRebind(retainedPending, catalog, budget);
            // A fresh complete authority/inventory/corpus proof authorizes discarding this exact disposable pending image.
            proof.RequireCurrent(database, budget);
            resources.DeleteOwnedFile(pending, () => File.Delete(pending));
        }
        if (File.Exists(current))
        { NativeTextFileIO.VerifyRegularFile(current); }
        budget.ChargeBytes(NativeSerialization.Measure(catalog));
        NativeTextFileIO.WriteEnvelope(pending, catalog, options.Value.MaximumOwnerReceiptBytes, options, resources);
        budget.ChargeBytes(new FileInfo(pending).Length);
        var verified = NativeTextFileIO.ReadEnvelope<NativeTextOnlineCatalog>(pending,
            options.Value.MaximumOwnerReceiptBytes);
        ValidateShape(verified);
        proof.RequireExact(verified, budget);
        proof.RequireCurrent(database, budget);
        budget.Check();
        observer?.Invoke(NativeTextFaultStage.OnlineCatalogPendingFlushed);
        resources.MutatePhysical(() => File.Move(pending, current, overwrite: true), budget);
        budget.Check();
    }

    internal static void RequireCurrentCatalog(string root, NativeTextOnlineCatalog expected,
        ReadExecutionBudget budget, IOptions<NativeTextExecutionOptions> options)
    {
        if (File.Exists(Path.Combine(root, PendingFile)))
        { throw NativeTextErrors.Ownership(); }
        RequireRebind(Read(root, budget, options), expected, budget);
    }

    private static void RequireRebind(NativeTextOnlineCatalog original, NativeTextOnlineCatalog fresh,
        ReadExecutionBudget budget)
    {
        ValidateShape(original);
        ValidateShape(fresh);
        if (original.Scope.ReadGeneration > fresh.Scope.ReadGeneration || original.Scope.Position > fresh.Scope.Position
            || original.AppliedPosition > fresh.AppliedPosition || original.ThroughSequence > fresh.ThroughSequence)
        { throw NativeTextErrors.Mismatch(); }
        var normalized = original with
        {
            Scope = original.Scope with { ReadGeneration = fresh.Scope.ReadGeneration, Position = fresh.Scope.Position },
            AppliedPosition = fresh.AppliedPosition,
            ThroughSequence = fresh.ThroughSequence
        };
        budget.ChargeBytes(NativeSerialization.Measure(normalized));
        budget.ChargeBytes(NativeSerialization.Measure(fresh));
        if (!NativeSerialization.Serialize(normalized).AsSpan().SequenceEqual(NativeSerialization.Serialize(fresh)))
        { throw NativeTextErrors.Mismatch(); }
        budget.Check();
    }

    private static void ValidateShape(NativeTextOnlineCatalog catalog)
    {
        if (catalog.FormatVersion != NativeTextProtocol.FormatVersion || catalog.BuildCommandId == Guid.Empty
            || catalog.ConsumerGeneration <= UnissuedConsumerGeneration || catalog.AppliedPosition < InitialAppliedPosition || catalog.ThroughSequence < InitialThroughSequence
            || catalog.NodeId != catalog.Scope.NodeId || catalog.Consumer.Partition != catalog.Scope.Partition
            || !NativeTextValidation.IsGenerationLeaf(catalog.Leaf)
            || !NativeTextIncrementalDigest.IsCanonical(catalog.ManifestSha256))
        { throw NativeTextErrors.Corrupt(); }
        NativeTextValidation.ValidateScope(catalog.Scope, catalog.NodeId);
    }
}
