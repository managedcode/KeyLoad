using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextSelectedSourceAdmission
{
    internal static (string Leaf, TextIncrementalEnrollment Enrollment) Select(DatabaseEngine database,
        string root, SearchRequest request, ReadExecutionBudget budget, IOptions<TextExecutionOptions> options)
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
        if (selected is null || enrollment is null
            || File.Exists(Path.Combine(root, selected, TextIncrementalProtocol.IntentFile)))
        {
            throw TextErrors.Mismatch();
        }
        return (selected, enrollment);
    }

    internal static TextIncrementalManifest ReadManifest(string root, string leaf, TextIncrementalEnrollment enrollment,
        SearchRequest request, ReadExecutionBudget budget, IOptions<TextExecutionOptions> options)
    {
        var selection = request.TextIndex ?? throw TextErrors.Corrupt();
        var manifest = TextIncrementalMetadata.ReadManifest(Path.Combine(root, leaf), options.Value.MaximumDiskBytes, budget);
        TextIncrementalValidation.Manifest(manifest, enrollment.Scope, selection.Consumer,
            selection.Generation, enrollment.Placement, budget, options);
        if (manifest.Bootstrap)
        {
            throw TextErrors.Mismatch();
        }
        return manifest;
    }

    /// <summary>Admits the generation when its authority still holds and no source change follows its manifest.</summary>
    internal static void RequireCurrent(DatabaseEngine database, IKeyValueView view, TextIncrementalEnrollment enrollment,
        TextIncrementalManifest manifest, SearchRequest request, ReadExecutionBudget budget)
    {
        var selection = request.TextIndex ?? throw TextErrors.Corrupt();
        var node = database.Store.Identity.NodeId;
        var maintenance = new TextIndexMaintenanceRequest(enrollment.BuildCommandId, selection.Consumer,
            request.Collection, request.TextField!, selection.Generation, node, enrollment.Placement,
            TextIndexMaintenanceMode.Restore);
        var source = TextSeedCollector.CaptureMetadataView(database, view, manifest.Scope.PrincipalId,
            new(selection.Consumer, selection.Generation, request.Collection, request.TextField!, node, enrollment.Placement), budget);
        TextIncrementalSourceValidation.Authority(manifest, maintenance, source, budget);
        TextIncrementalSourceValidation.Current(manifest, source, budget);
        budget.Check();
    }
}
