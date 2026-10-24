using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextSelectedSourceAdmission
{
    internal static (string Leaf, TextIncrementalManifest Manifest) Capture(DatabaseEngine database,
        IKeyValueView view, string root, SearchRequest request, ReadExecutionBudget budget,
        IOptions<TextExecutionOptions> options)
    {
        var selection = request.TextIndex ?? throw TextErrors.Corrupt();
        var node = database.Store.Identity.NodeId;
        _ = TextIncrementalRoot.CheckRoot(root, node, options, budget);
        string? selected = null;
        TextIncrementalEnrollment? enrollment = null;
        foreach (var path in Directory.EnumerateDirectories(root))
        {
            budget.Check();
            var actual = TextIncrementalEnrollmentFiles.Read(path, node, budget, options);
            if (actual.Consumer != selection.Consumer || actual.Generation != selection.Generation)
            {
                continue;
            }
            if (selected is not null || actual.Scope.Collection != request.Collection || actual.Scope.Field != request.TextField)
            {
                throw TextErrors.Corrupt();
            }
            selected = Path.GetFileName(path);
            enrollment = actual;
        }
        if (selected is null || enrollment is null)
        {
            throw TextErrors.Mismatch();
        }
        var generationPath = Path.Combine(root, selected);
        if (File.Exists(Path.Combine(generationPath, TextIncrementalProtocol.IntentFile)))
        {
            throw TextErrors.Mismatch();
        }
        var maintenance = new TextIndexMaintenanceRequest(enrollment.BuildCommandId, selection.Consumer,
            request.Collection, request.TextField!, selection.Generation, node, enrollment.Placement,
            TextIndexMaintenanceMode.Restore);
        var manifest = TextIncrementalMetadata.ReadManifest(generationPath, options.Value.MaximumDiskBytes, budget);
        TextIncrementalValidation.Manifest(manifest, enrollment.Scope, selection.Consumer,
            selection.Generation, enrollment.Placement, database.Limits.MaxScanRecords, budget, options);
        if (manifest.Bootstrap)
        {
            throw TextErrors.Mismatch();
        }
        var source = TextSeedCollector.CaptureView(database, view, manifest.Scope.PrincipalId,
            new(selection.Consumer, selection.Generation, request.Collection, request.TextField!, node, enrollment.Placement), budget);
        TextIncrementalSourceValidation.Authority(manifest, maintenance, source, budget);
        TextIncrementalSourceValidation.CompleteCorpus(manifest, source, budget);
        var owner = TextOwnerFiles.ReadOwner(Path.Combine(generationPath, TextProtocol.OwnerFile),
            root, selected, node, options);
        TextInventory.Verify(generationPath, owner.OwnedPaths, manifest.Files, options, budget);
        budget.Check();
        return (selected, manifest);
    }
}
