using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextSelectedSourceAdmission
{
    internal static (string Leaf, NativeTextIncrementalManifest Manifest) Capture(DatabaseEngine database,
        IKeyValueView view, string root, SearchRequest request, ReadExecutionBudget budget,
        IOptions<NativeTextExecutionOptions> options)
    {
        var selection = request.TextIndex ?? throw NativeTextErrors.Corrupt();
        var node = database.Store.Identity.NodeId;
        _ = NativeTextIncrementalRoot.CheckRoot(root, node, options, budget);
        string? selected = null;
        NativeTextIncrementalEnrollment? enrollment = null;
        foreach (var path in Directory.EnumerateDirectories(root))
        {
            budget.Check();
            var actual = NativeTextIncrementalEnrollmentFiles.Read(path, node, budget, options);
            if (actual.Consumer != selection.Consumer || actual.Generation != selection.Generation)
            { continue; }
            if (selected is not null || actual.Scope.Collection != request.Collection || actual.Scope.Field != request.TextField)
            { throw NativeTextErrors.Corrupt(); }
            selected = Path.GetFileName(path);
            enrollment = actual;
        }
        if (selected is null || enrollment is null)
        { throw NativeTextErrors.Mismatch(); }
        var generationPath = Path.Combine(root, selected);
        if (File.Exists(Path.Combine(generationPath, NativeTextIncrementalProtocol.IntentFile)))
        { throw NativeTextErrors.Mismatch(); }
        var maintenance = new TextIndexMaintenanceRequest(enrollment.BuildCommandId, selection.Consumer,
            request.Collection, request.TextField!, selection.Generation, node, enrollment.Placement,
            TextIndexMaintenanceMode.Restore);
        var manifest = NativeTextIncrementalMetadata.ReadManifest(generationPath, options.Value.MaximumDiskBytes, budget);
        NativeTextIncrementalValidation.Manifest(manifest, enrollment.Scope, selection.Consumer,
            selection.Generation, enrollment.Placement, database.Limits.MaxScanRecords, budget, options);
        if (manifest.Bootstrap)
        { throw NativeTextErrors.Mismatch(); }
        var source = NativeTextSeedCollector.CaptureView(database, view, manifest.Scope.PrincipalId,
            new(selection.Consumer, selection.Generation, request.Collection, request.TextField!, node, enrollment.Placement), budget);
        NativeTextIncrementalSourceValidation.Authority(manifest, maintenance, source, budget);
        NativeTextIncrementalSourceValidation.CompleteCorpus(manifest, source, budget);
        var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(generationPath, NativeTextProtocol.OwnerFile),
            root, selected, node, options);
        NativeTextInventory.Verify(generationPath, owner.OwnedPaths, manifest.Files, options, budget);
        budget.Check();
        return (selected, manifest);
    }
}
