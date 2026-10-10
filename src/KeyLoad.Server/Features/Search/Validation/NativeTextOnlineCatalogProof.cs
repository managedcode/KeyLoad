using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOnlineCatalogProof : IDisposable
{
    private readonly NativeTextOnlineGenerationPin retained;
    private readonly byte[] immutableCurrent;
    private readonly byte[] immutableCatalog;
    private readonly OnlineTextIndexMaintenanceRequest request;
    private readonly string principalId;
    private readonly TimeProvider clock;

    private NativeTextOnlineCatalogProof(NativeTextOnlineGenerationPin retained,
        OnlineTextCurrentPublication current, OnlineTextIndexMaintenanceRequest request,
        NativeTextOnlineCatalog catalog, string principalId, TimeProvider clock)
    {
        this.retained = retained;
        this.request = request;
        this.principalId = principalId;
        this.clock = clock;
        immutableCurrent = NativeSerialization.Serialize(current);
        immutableCatalog = NativeSerialization.Serialize(catalog);
        Catalog = catalog;
    }

    internal NativeTextOnlineCatalog Catalog { get; }

    internal static NativeTextOnlineCatalogProof Capture(DatabaseEngine database, string principalId,
        OnlineTextIndexMaintenanceRequest request, NativeTextOnlineGeneration generation,
        ReadExecutionBudget budget, TimeProvider clock, IOptions<NativeTextExecutionOptions> options)
    {
        var retained = generation.Pin(budget);
        try
        {
            var current = ReadCurrent(database, principalId, request, budget, clock);
            RequireSame(current, generation.Original, budget);
            var original = database.ReadOnlineTextOriginalOutcome(principalId, request, budget)
                ?? throw NativeTextErrors.Ownership();
            var result = original.Get<OnlineTextIndexMaintenanceResult>();
            NativeTextOnlineCatalogSource.RequireOriginal(retained, current, result, database.Limits.MaxScanRecords, budget, options);
            var fresh = CaptureSeed(database, principalId, request, budget);
            NativeTextOnlineCatalogSource.RequireFresh(current, result, generation.Manifest, fresh, budget);
            var scope = new TextProjectionScope(request.NodeId, fresh.Incarnation, fresh.DataEpoch,
                fresh.ReadGeneration, fresh.Position, request.Consumer.Partition, request.Collection,
                request.Field, fresh.PrincipalId, fresh.PolicyEpoch, fresh.SchemaVersion);
            var catalog = new NativeTextOnlineCatalog(NativeTextProtocol.FormatVersion, request.NodeId,
                scope, request.Placement, request.Consumer, request.ConsumerGeneration,
                current.Authority.Leaf, fresh.AppliedPosition, fresh.UpperSequence,
                current.Authority.ManifestSha256, current.CommandId);
            budget.ChargeBytes(NativeSerialization.Measure(current));
            budget.ChargeBytes(NativeSerialization.Measure(catalog));
            var proof = new NativeTextOnlineCatalogProof(retained, current, request, catalog, principalId, clock);
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

    internal static NativeTextOnlineCatalogProof CaptureFromView(DatabaseEngine database, IKeyValueView raw,
        PrincipalRecord principal, OnlineTextIndexMaintenanceRequest request, NativeTextOnlineGeneration generation,
        ReadExecutionBudget budget, TimeProvider clock, IOptions<NativeTextExecutionOptions> options)
    {
        var retained = generation.Pin(budget);
        try
        {
            var view = budget.CreateView(raw);
            var current = database.ReadOnlineTextCurrentPublication(view, principal, request, budget)
                ?? throw NativeTextErrors.Ownership();
            RequireSame(current, generation.Original, budget);
            var original = database.ReadOnlineTextOriginalOutcome(view, principal, request, budget)
                ?? throw NativeTextErrors.Ownership();
            var result = original.Get<OnlineTextIndexMaintenanceResult>();
            NativeTextOnlineCatalogSource.RequireOriginal(retained, current, result,
                database.Limits.MaxScanRecords, budget, options);
            var fresh = NativeTextSeedCollector.CaptureOnlineQueryView(database, raw, principal.Id,
                new(request.Consumer, request.ConsumerGeneration, request.Collection, request.Field,
                    request.NodeId, request.Placement), budget);
            NativeTextOnlineCatalogSource.RequireFresh(current, result, generation.Manifest, fresh, budget);
            var scope = new TextProjectionScope(request.NodeId, fresh.Incarnation, fresh.DataEpoch,
                fresh.ReadGeneration, fresh.Position, request.Consumer.Partition, request.Collection,
                request.Field, fresh.PrincipalId, fresh.PolicyEpoch, fresh.SchemaVersion);
            var catalog = new NativeTextOnlineCatalog(NativeTextProtocol.FormatVersion, request.NodeId,
                scope, request.Placement, request.Consumer, request.ConsumerGeneration, current.Authority.Leaf,
                fresh.AppliedPosition, fresh.UpperSequence, current.Authority.ManifestSha256, current.CommandId);
            budget.ChargeBytes(NativeSerialization.Measure(current));
            budget.ChargeBytes(NativeSerialization.Measure(catalog));
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
        budget.ChargeBytes(NativeSerialization.Measure(current));
        if (!NativeSerialization.Serialize(current).AsSpan().SequenceEqual(immutableCurrent))
        { throw NativeTextErrors.Mismatch(); }
        budget.Check();
    }

    internal void RequireExact(NativeTextOnlineCatalog catalog, ReadExecutionBudget budget)
    {
        retained.RequireActive();
        budget.ChargeBytes(NativeSerialization.Measure(catalog));
        if (!NativeSerialization.Serialize(catalog).AsSpan().SequenceEqual(immutableCatalog))
        { throw NativeTextErrors.Mismatch(); }
        budget.Check();
    }

    private static OnlineTextCurrentPublication ReadCurrent(DatabaseEngine database, string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget, TimeProvider clock)
        => database.Store.Read(raw =>
        {
            var view = budget.CreateView(raw);
            var principal = database.Principal(view, principalId, clock.GetUtcNow());
            return database.ReadOnlineTextCurrentPublication(view, principal, request, budget)
                ?? throw NativeTextErrors.Ownership();
        });

    private static void RequireSame(OnlineTextCurrentPublication current,
        OnlineTextCurrentPublication expected, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(NativeSerialization.Measure(current));
        budget.ChargeBytes(NativeSerialization.Measure(expected));
        if (!NativeSerialization.Serialize(current).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected)))
        { throw NativeTextErrors.Mismatch(); }
    }

    private static NativeTextSeedCapture CaptureSeed(DatabaseEngine database, string principalId,
        OnlineTextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        NativeTextOnlineSourcePin? native = null;
        NativeTextSeedCapture? fresh = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            native = NativeTextOnlineSourcePin.Capture(database, principalId,
                new(request.Consumer, request.ConsumerGeneration, request.Collection, request.Field,
                    request.NodeId, request.Placement), budget);
            fresh = native.ReadSeed();
        }, failures);
        if (native is not null)
        { ServerFailureObserver.Observe(native.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return fresh ?? throw NativeTextErrors.Corrupt();
    }

    public void Dispose() => retained.Dispose();
}
