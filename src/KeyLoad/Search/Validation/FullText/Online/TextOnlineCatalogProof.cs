using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Query.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal sealed class TextOnlineCatalogProof : IDisposable
{
    private readonly TextOnlineGenerationPin retained;
    private readonly byte[] immutableCurrent;
    private readonly byte[] immutableCatalog;
    private readonly OnlineTextIndexMaintenanceRequest request;
    private readonly string principalId;
    private readonly TimeProvider clock;

    private TextOnlineCatalogProof(TextOnlineGenerationPin retained,
        OnlineTextCurrentPublication current, OnlineTextIndexMaintenanceRequest request,
        TextOnlineCatalog catalog, string principalId, TimeProvider clock)
    {
        this.retained = retained;
        this.request = request;
        this.principalId = principalId;
        this.clock = clock;
        immutableCurrent = OrleansSerialization.Serialize(current);
        immutableCatalog = OrleansSerialization.Serialize(catalog);
        Catalog = catalog;
    }

    internal TextOnlineCatalog Catalog
    {
        get;
    }

    internal static TextOnlineCatalogProof Capture(DatabaseEngine database, string principalId,
        OnlineTextIndexMaintenanceRequest request, TextOnlineGeneration generation,
        ReadExecutionBudget budget, TimeProvider clock, IOptions<TextExecutionOptions> options)
    {
        var retained = generation.Pin(budget);
        try
        {
            var current = ReadCurrent(database, principalId, request, budget, clock);
            RequireSame(current, generation.Original, budget);
            var original = database.ReadOnlineTextOriginalOutcome(principalId, request, budget)
                ?? throw TextErrors.Ownership();
            var result = original.Get<OnlineTextIndexMaintenanceResult>();
            TextOnlineCatalogSource.RequireOriginal(retained, current, result, budget, options);
            var fresh = CaptureSeed(database, principalId, request, budget);
            TextOnlineCatalogSource.RequireFresh(current, result, generation.Manifest, fresh, budget);
            var scope = new TextProjectionScope(request.NodeId, fresh.Incarnation, fresh.DataEpoch,
                fresh.ReadGeneration, fresh.Position, request.Consumer.Partition, request.Collection,
                request.Field, fresh.PrincipalId, fresh.PolicyEpoch, fresh.SchemaVersion);
            var catalog = new TextOnlineCatalog(TextProtocol.FormatVersion, request.NodeId,
                scope, request.Placement, request.Consumer, request.ConsumerGeneration,
                current.Authority.Leaf, fresh.AppliedPosition, fresh.UpperSequence,
                current.Authority.ManifestSha256, current.CommandId);
            budget.ChargeBytes(OrleansSerialization.Measure(current));
            budget.ChargeBytes(OrleansSerialization.Measure(catalog));
            var proof = new TextOnlineCatalogProof(retained, current, request, catalog, principalId, clock);
            proof.RequireCurrent(database, budget);
            return proof;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(retained.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal static TextOnlineCatalogProof CaptureFromView(DatabaseEngine database, IKeyValueView raw,
        PrincipalRecord principal, OnlineTextIndexMaintenanceRequest request, TextOnlineGeneration generation,
        ReadExecutionBudget budget, TimeProvider clock, IOptions<TextExecutionOptions> options)
    {
        var retained = generation.Pin(budget);
        try
        {
            var view = budget.CreateView(raw);
            var current = database.ReadOnlineTextCurrentPublication(view, principal, request, budget)
                ?? throw TextErrors.Ownership();
            RequireSame(current, generation.Original, budget);
            var original = database.ReadOnlineTextOriginalOutcome(view, principal, request, budget)
                ?? throw TextErrors.Ownership();
            var result = original.Get<OnlineTextIndexMaintenanceResult>();
            TextOnlineCatalogSource.RequireOriginal(retained, current, result, budget, options);
            var fresh = TextSeedCollector.CaptureOnlineQueryView(database, raw, principal.Id,
                new(request.Consumer, request.ConsumerGeneration, request.Collection, request.Field,
                    request.NodeId, request.Placement), budget);
            TextOnlineCatalogSource.RequireFresh(current, result, generation.Manifest, fresh, budget);
            var scope = new TextProjectionScope(request.NodeId, fresh.Incarnation, fresh.DataEpoch,
                fresh.ReadGeneration, fresh.Position, request.Consumer.Partition, request.Collection,
                request.Field, fresh.PrincipalId, fresh.PolicyEpoch, fresh.SchemaVersion);
            var catalog = new TextOnlineCatalog(TextProtocol.FormatVersion, request.NodeId,
                scope, request.Placement, request.Consumer, request.ConsumerGeneration, current.Authority.Leaf,
                fresh.AppliedPosition, fresh.UpperSequence, current.Authority.ManifestSha256, current.CommandId);
            budget.ChargeBytes(OrleansSerialization.Measure(current));
            budget.ChargeBytes(OrleansSerialization.Measure(catalog));
            return new(retained, current, request, catalog, principal.Id, clock);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(retained.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal void RequireCurrent(DatabaseEngine database, ReadExecutionBudget budget)
    {
        retained.RequireActive();
        var current = ReadCurrent(database, principalId, request, budget, clock);
        budget.ChargeBytes(OrleansSerialization.Measure(current));
        if (!OrleansSerialization.Serialize(current).AsSpan().SequenceEqual(immutableCurrent))
        {
            throw TextErrors.Mismatch();
        }
        budget.Check();
    }

    internal void RequireExact(TextOnlineCatalog catalog, ReadExecutionBudget budget)
    {
        retained.RequireActive();
        budget.ChargeBytes(OrleansSerialization.Measure(catalog));
        if (!OrleansSerialization.Serialize(catalog).AsSpan().SequenceEqual(immutableCatalog))
        {
            throw TextErrors.Mismatch();
        }
        budget.Check();
    }

    private static OnlineTextCurrentPublication ReadCurrent(DatabaseEngine database, string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget, TimeProvider clock)
        => database.Store.Read(raw =>
        {
            var view = budget.CreateView(raw);
            var principal = database.Principal(view, principalId, clock.GetUtcNow());
            return database.ReadOnlineTextCurrentPublication(view, principal, request, budget)
                ?? throw TextErrors.Ownership();
        });

    private static void RequireSame(OnlineTextCurrentPublication current,
        OnlineTextCurrentPublication expected, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(OrleansSerialization.Measure(current));
        budget.ChargeBytes(OrleansSerialization.Measure(expected));
        if (!OrleansSerialization.Serialize(current).AsSpan().SequenceEqual(OrleansSerialization.Serialize(expected)))
        {
            throw TextErrors.Mismatch();
        }
    }

    private static TextSeedCapture CaptureSeed(DatabaseEngine database, string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        TextOnlineSourcePin? execution = null;
        TextSeedCapture? fresh = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            execution = TextOnlineSourcePin.Capture(database, principalId,
                new(request.Consumer, request.ConsumerGeneration, request.Collection, request.Field,
                    request.NodeId, request.Placement), budget);
            fresh = execution.ReadSeed();
        }, failures);
        if (execution is not null)
        {
            ServerFailureObserver.Observe(execution.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return fresh ?? throw TextErrors.Corrupt();
    }

    public void Dispose() => retained.Dispose();
}
