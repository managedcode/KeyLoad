using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Security;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class C1OutcomeInspectionFixture : IDisposable
{
    private const string RootPrefix = "keyload-c1-outcome-inspection-";
    private const string GuidFormat = "N";
    private const string StoreDirectoryName = "database";
    private const string OuterOwnerName = "node.owner.lock";
    private const string StoreOwnerName = "owner.lock";
    private const int MaximumPhaseLineBytes = 512;
    private const int MaximumPhaseLines = 32;
    private const string PhaseBudgetMessage = "The outcome inspection phase observation exceeded its fixed bound.";
    private const string AdminId = "inspection-admin";
    private const string AdminSecret = "inspection.admin-secret-32-characters";
    private const string TenantId = "inspection-tenant";
    private const string DatabaseId = "inspection-database";
    private const string DomainId = "inspection-domain";
    private const string ResourceName = "inspection-resource";
    private const string SuccessMessage = "The native outcome seed did not succeed.";
    private const string UninitializedMessage = "The native outcome fixture has not completed initialization.";
    private ZoneTreeStore? store;
    private DatabaseEngine? InitializedDatabase { get; set; }
    private bool rootOwned;
    private bool storeClosed;
    private int phaseLineCount;
    private bool phaseBudgetReported;
    private readonly List<Exception> phaseFailures = [];

    private C1OutcomeInspectionFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        DirectoryPath = Path.Combine(Root, StoreDirectoryName);
        OuterOwnerLockPath = Path.Combine(Root, OuterOwnerName);
        Incarnation = Guid.NewGuid();
        CommandId = Guid.NewGuid();
    }

    internal static async Task RunOwnedAsync(Func<C1OutcomeInspectionFixture, Task> body)
    {
        var failures = new List<Exception>();
        try
        {
            using var fixture = new C1OutcomeInspectionFixture();
            ServerFailureObserver.Observe(fixture.Initialize, failures);
            if (failures.Count == 0)
            {
                await ServerFailureObserver.ObserveAsync(() => body(fixture), failures).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (!C1OutcomeInspectionFailures.ContainsFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (C1OutcomeInspectionFailures.ContainsFatal(error))
        { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal string Root { get; }
    internal string DirectoryPath { get; }
    internal string OuterOwnerLockPath { get; }
    internal Guid Incarnation { get; }
    internal ZoneTreeStore Store => store ?? throw new InvalidOperationException(UninitializedMessage);
    internal DatabaseEngine Database => InitializedDatabase ?? throw new InvalidOperationException(UninitializedMessage);
    internal StoreIdentity Identity => Store.Identity;
    internal Guid CommandId { get; }
    internal long Position { get; private set; }

    private void Initialize()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(CreateOwnedRoot, failures);
        if (rootOwned && failures.Count == 0)
        { ServerFailureObserver.Observe(SeedNativeOutcome, failures); }
        if (failures.Count == 0)
        { return; }
        var cleanupFailures = new List<Exception>();
        ServerFailureObserver.Observe(CloseStore, cleanupFailures);
        if (rootOwned && storeClosed)
        { ServerFailureObserver.Observe(DeleteOwnedRoot, cleanupFailures); }
        failures.AddRange(cleanupFailures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void CreateOwnedRoot()
    {
        Directory.CreateDirectory(Root);
        rootOwned = true;
    }

    private void SeedNativeOutcome()
    {
        using (File.Create(OuterOwnerLockPath))
        { }
        var nativeStore = new ZoneTreeStore(new ZoneTreeStoreOptions(DirectoryPath) { Incarnation = Incarnation });
        store = nativeStore;
        var engine = new DatabaseEngine(nativeStore, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
        InitializedDatabase = engine;
        engine.Bootstrap(new(AdminId, TenantId, [new("*", "*", Capability.All)], ["*"])
        { ClusterAdministrator = true }, DatabaseEngine.Credential(AdminId, AdminId, AdminSecret));
        var request = new ConfigureResourceRequest(TenantId, DatabaseId,
            new(ResourceName, ResourceKind.Collection, DomainId));
        var operation = new ReplicatedOperation(CommandId, OperationKind.ConfigureResource, AdminId,
            TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options));
        var result = engine.Apply(operation);
        if (result.Error is not null || engine.ResolveOutcome(operation).Error is not null)
        { throw new InvalidOperationException(SuccessMessage); }
        Position = nativeStore.Position;
    }

    internal void RecordOwnerPhase(string line)
    {
        var writer = Console.Error;
        if (phaseLineCount >= MaximumPhaseLines
            || Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(writer.NewLine) > MaximumPhaseLineBytes)
        {
            if (!phaseBudgetReported)
            {
                phaseBudgetReported = true;
                phaseFailures.Add(new InvalidOperationException(PhaseBudgetMessage));
            }
            return;
        }
        phaseLineCount++;
        ServerFailureObserver.Observe(() => writer.WriteLine(line), phaseFailures);
    }

    internal void CloseStore()
    {
        if (store is null)
        {
            storeClosed = true;
            return;
        }
        if (storeClosed)
        { return; }
        store.Dispose();
        storeClosed = true;
    }

    public void Dispose()
    {
        var cleanupFailures = new List<Exception>();
        try
        { CloseStore(); }
        catch (Exception error) when (!C1OutcomeInspectionFailures.ContainsFatal(error))
        { cleanupFailures.Add(error); }
        catch (Exception error) when (C1OutcomeInspectionFailures.ContainsFatal(error))
        { cleanupFailures.Add(error); }
        if (rootOwned && storeClosed)
        { ServerFailureObserver.Observe(DeleteOwnedRoot, cleanupFailures); }
        cleanupFailures.AddRange(phaseFailures);
        ServerFailureObserver.ThrowIfAny(cleanupFailures);
    }

    private void DeleteOwnedRoot()
    {
        if (!Directory.Exists(Root))
        {
            rootOwned = false;
            return;
        }
        VerifyOwnedLocksReleased();
        Directory.Delete(Root, recursive: true);
        rootOwned = false;
    }

    private void VerifyOwnedLocksReleased()
    {
        if (File.Exists(OuterOwnerLockPath))
        {
            using var outer = C1OutcomeInspectionOwnerPhase.Acquire(this,
                C1OutcomeInspectionOwnerRole.FinalOuter, OuterOwnerLockPath);
        }
        var storeOwnerPath = Path.Combine(DirectoryPath, StoreOwnerName);
        if (File.Exists(storeOwnerPath))
        {
            using var inner = C1OutcomeInspectionOwnerPhase.Acquire(this,
                C1OutcomeInspectionOwnerRole.FinalDatabase, storeOwnerPath);
        }
    }
}
