using KeyLoad.Core;
using KeyLoad.Core.Search;

namespace KeyLoad.Server.Search;

internal sealed partial class TextOnlineCatalogOwner(DatabaseEngine database, TextOnlineRoot root,
    ServerRuntimeOptions options, TimeProvider clock, TextResourceOwnership resources,
    Action<TextFaultStage>? observer)
{
    private readonly Lock generationGate = new();
    private readonly Dictionary<string, TextOnlineGeneration> generations = new(StringComparer.Ordinal);
    private TextOnlineGeneration? activeGeneration;

    internal async Task ReconcileCommittedAsync(TextOnlineSession session)
    {
        await session.CloseCommandAdmissionAndJoinAsync();
        observer?.Invoke(TextFaultStage.CanonicalOnlinePublicationAcknowledged);
        var current = ReadCommittedCurrent(session.PrincipalId, session.Request, session.Budget);
        if (current.CommandId != session.Request.CommandId || current.Authority.Leaf != session.Leaf)
        {
            throw TextErrors.Mismatch();
        }
        PublishCurrentCatalog(current, session.Request, session.Budget, session);
    }

    internal OnlineTextCurrentPublication ReadCommittedCurrent(string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
        => database.Store.Read(raw =>
        {
            var view = budget.CreateView(raw);
            var principal = database.Principal(view, principalId, clock.GetUtcNow());
            return database.ReadOnlineTextCurrentPublication(view, principal, request, budget)
                ?? throw TextErrors.Ownership();
        });

    internal TextOnlineGeneration PublishCurrentCatalog(OnlineTextCurrentPublication current,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget, TextOnlineSession? session)
    {
        var generation = RequireGeneration(current, budget);
        TextOnlineGeneration? previous = null;
        TextOnlineCatalogProof? proof = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            proof = TextOnlineCatalogProof.Capture(database, current.PrincipalId,
                request, generation, budget, clock, options.RuntimeText);
            TextOnlineCatalogFiles.Publish(root.Directory, proof, database, budget, options.RuntimeText, resources, observer);
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
        {
            ServerFailureObserver.Observe(proof.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        if (previous is not null && !ReferenceEquals(previous, generation))
        {
            BeginRetirement(previous);
        }
        return generation;
    }

    internal TextOnlineGeneration RequireGeneration(OnlineTextCurrentPublication current,
        ReadExecutionBudget budget)
    {
        lock (generationGate)
        {
            if (generations.TryGetValue(current.Authority.Leaf, out var existing))
            {
                return existing;
            }
            if (generations.Count >= options.RuntimeText.Value.MaximumGenerations)
            {
                throw TextErrors.BoundExceeded();
            }
            var path = Path.Combine(root.Directory, current.Authority.Leaf);
            var manifest = TextIncrementalMetadata.ReadManifest(path, options.RuntimeText.Value.MaximumDiskBytes, budget);
            var actual = new TextOnlineGeneration(root.Directory, current, manifest, options.RuntimeText, resources);
            generations.Add(current.Authority.Leaf, actual);
            return actual;
        }
    }
}
