using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class TextIncrementalRelease
{
    internal static void Execute(DatabaseEngine database, string principal, TextIndexMaintenanceRequest request,
        string root, TextIncrementalSessions sessions, ReadExecutionBudget budget,
        IOptions<TextExecutionOptions> options, TextResourceOwnership? resources = null,
        Action<string>? releaseGeneration = null)
    {
        budget.Check();
        if (request.Mode != TextIndexMaintenanceMode.Release)
        {
            throw TextErrors.Mismatch();
        }
        TextSeedCollector.RequireReleased(database, principal,
            new(request.Consumer, request.IndexGeneration, request.Collection, request.Field,
                request.NodeId, request.Placement), budget);
        var leaf = TextIncrementalEnrollmentFiles.Find(root, request, budget, options);
        if (leaf is null)
        {
            return;
        }
        sessions.RetireGeneration(leaf);
        _ = TextIncrementalRoot.CheckRoot(root, request.NodeId, options, budget);
        var path = Path.Combine(root, leaf);
        TextFileIO.VerifyDirectory(path);
        budget.Check();
        if (resources is null)
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            resources.DeleteOwnedDirectory(path, () => Directory.Delete(path, recursive: true));
        }
        releaseGeneration?.Invoke(leaf);
        budget.Check();
        _ = TextIncrementalRoot.CheckRoot(root, request.NodeId, options, budget);
    }
}
