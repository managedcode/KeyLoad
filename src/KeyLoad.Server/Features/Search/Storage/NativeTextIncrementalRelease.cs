using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalRelease
{
    internal static void Execute(DatabaseEngine database, string principal, TextIndexMaintenanceRequest request,
        string root, NativeTextIncrementalSessions sessions, ReadExecutionBudget budget,
        IOptions<NativeTextExecutionOptions> options)
    {
        budget.Check();
        if (request.Mode != TextIndexMaintenanceMode.Release)
        { throw NativeTextErrors.Mismatch(); }
        NativeTextSeedCollector.RequireReleased(database, principal,
            new(request.Consumer, request.IndexGeneration, request.Collection, request.Field,
                request.NodeId, request.Placement), budget);
        var leaf = NativeTextIncrementalEnrollmentFiles.Find(root, request, budget, options);
        if (leaf is null)
        { return; }
        sessions.RetireGeneration(leaf);
        _ = NativeTextIncrementalRoot.CheckRoot(root, request.NodeId, options, budget);
        var path = Path.Combine(root, leaf);
        NativeTextFileIO.VerifyDirectory(path);
        budget.Check();
        Directory.Delete(path, recursive: true);
        budget.Check();
        _ = NativeTextIncrementalRoot.CheckRoot(root, request.NodeId, options, budget);
    }
}
