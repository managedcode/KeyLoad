using KeyLoad.Core;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineCatalogOwner(DatabaseEngine database, NativeTextOnlineRoot root,
    ServerRuntimeOptions options, TimeProvider clock, NativeTextResourceOwnership resources,
    Action<NativeTextFaultStage>? observer)
{
    private readonly Lock generationGate = new();
    private readonly Dictionary<string, NativeTextOnlineGeneration> generations = new(StringComparer.Ordinal);
    private NativeTextOnlineGeneration? activeGeneration;

    internal async Task ReconcileCommittedAsync(NativeTextOnlineSession session)
    {
        await session.CloseCommandAdmissionAndJoinAsync().ConfigureAwait(false);
        observer?.Invoke(NativeTextFaultStage.CanonicalOnlinePublicationAcknowledged);
        var current = ReadCommittedCurrent(session.PrincipalId, session.Request, session.Budget);
        if (current.CommandId != session.Request.CommandId || current.Authority.Leaf != session.Leaf)
        { throw NativeTextErrors.Mismatch(); }
        PublishCurrentCatalog(current, session.Request, session.Budget, session);
    }

    internal OnlineTextCurrentPublication ReadCommittedCurrent(string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
        => database.Store.Read(raw =>
        {
            var view = budget.CreateView(raw);
            var principal = database.Principal(view, principalId, clock.GetUtcNow());
            return database.ReadOnlineTextCurrentPublication(view, principal, request, budget)
                ?? throw NativeTextErrors.Ownership();
        });

    internal NativeTextOnlineGeneration PublishCurrentCatalog(OnlineTextCurrentPublication current,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget, NativeTextOnlineSession? session)
    {
        var generation = RequireGeneration(current, budget);
        NativeTextOnlineGeneration? previous = null;
        NativeTextOnlineCatalogProof? proof = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            proof = NativeTextOnlineCatalogProof.Capture(database, current.PrincipalId,
                request, generation, budget, clock, options.NativeText);
            NativeTextOnlineCatalogFiles.Publish(root.Directory, proof, database, budget, options.NativeText, resources, observer);
            lock (generationGate)
            {
                previous = activeGeneration;
                activeGeneration = generation;
                if (session is not null)
                {
                    generation.AdoptReservation(session.GenerationReservation);
                    session.GenerationReservation = null;
                    session.CatalogCommitted = true;
                }
            }
        }, failures);
        if (proof is not null)
        { ServerFailureObserver.Observe(proof.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        if (previous is not null && !ReferenceEquals(previous, generation))
        { BeginRetirement(previous); }
        return generation;
    }

    internal NativeTextOnlineGeneration RequireGeneration(OnlineTextCurrentPublication current,
        ReadExecutionBudget budget)
    {
        lock (generationGate)
        {
            if (generations.TryGetValue(current.Authority.Leaf, out var existing))
            { return existing; }
            if (generations.Count >= options.NativeText.Value.MaximumGenerations)
            { throw NativeTextErrors.BoundExceeded(); }
            var path = Path.Combine(root.Directory, current.Authority.Leaf);
            var manifest = NativeTextIncrementalMetadata.ReadManifest(path, options.NativeText.Value.MaximumDiskBytes, budget);
            var actual = new NativeTextOnlineGeneration(root.Directory, current, manifest, options.NativeText, resources);
            generations.Add(current.Authority.Leaf, actual);
            return actual;
        }
    }
}
