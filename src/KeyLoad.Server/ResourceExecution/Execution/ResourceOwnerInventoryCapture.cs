using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Retains bounded captured metadata without conflating observed owners with current placement.</summary>
internal sealed class ResourceOwnerInventoryCapture(ResourceOwnerInventoryControlV1 control, int maximumResults)
{
    private const int SupportedWriterVersion = 1;
    private const int NonAdvancingKeyComparison = 0;
    private readonly HashSet<Guid> observedNodes = [];
    private readonly List<ResourceOwnerInventoryEntryV1> entries = [];
    private readonly List<ResourceOwnerInventoryOwnerCutV1> cuts = [];
    private readonly Dictionary<(ResourceLifetimeV1, PartitionRef), List<ResourceOwnerInventoryEntryV1>> identities = [];
    private long retainedBytes;

    internal void Initialize(ReadExecutionBudget work) => Retain(control, work);

    internal void AddCut(ResourceOwnerInventoryOwnerCutV1 cut, ReadExecutionBudget work)
    {
        if (!observedNodes.Add(cut.NodeId))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        Retain(cut, work);
        cuts.Add(cut);
    }

    internal void AddPage(RegisteredPhysicalOwnerV1 owner, ResourceOwnerInventoryPageV1 page,
        ResourceOwnerInventoryOwnerCutV1? originalCut, ReadExecutionBudget work)
    {
        if (page.Cut is null || page.Cut.NodeId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        if (page.Cut.WriterVersion != SupportedWriterVersion)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        if (page.Entries.IsDefault || page.Entries.Length > control.Request.PageSize
            || !PhysicalOwnerEntryValidation.SameOwner(owner.Owner, page.Cut.ObservedOwner)
            || originalCut is not null && !Same(originalCut, page.Cut))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        foreach (var entry in page.Entries)
        {
            work.Check();
            if (entry.Lifetime != control.Lifetime
                || !PhysicalOwnerEntryValidation.SameOwner(entry.ObservedOwner, owner.Owner))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryNativeProtocol.Unavailable);
            }
            AddEntry(entry, work);
        }
    }

    internal void RequireContinuation(ResourceOwnerInventoryCursorV1? previous,
        ResourceOwnerInventoryCursorV1 next, ResourceOwnerInventoryOwnerCutV1 cut)
    {
        if (next.InventoryId != control.Request.InventoryId || next.Incarnation != control.Lifetime.Incarnation
            || next.ExpiresAt != control.Request.ExpiresAt || !Same(next.Cut, cut) || next.AfterKey.IsEmpty
            || previous is not null && next.AfterKey.Span.SequenceCompareTo(previous.AfterKey.Span) <= NonAdvancingKeyComparison)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
    }

    internal ResourceOwnerInventoryResultV1 Complete(ReadExecutionBudget work)
    {
        var result = new ResourceOwnerInventoryResultV1(control, cuts.ToImmutableArray(), entries.ToImmutableArray(),
            true, ResourceOwnerInventoryCoverageKind.ConservativeMayContain);
        work.CheckResult(result);
        return result;
    }

    private void AddEntry(ResourceOwnerInventoryEntryV1 entry, ReadExecutionBudget work)
    {
        var key = (entry.Lifetime, entry.Partition);
        if (!identities.TryGetValue(key, out var observed))
        {
            observed = [];
            identities.Add(key, observed);
        }
        var prior = observed.Find(value => PhysicalOwnerEntryValidation.SameOwner(value.ObservedOwner, entry.ObservedOwner));
        if (prior is not null)
        {
            if (!Same(prior, entry))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryNativeProtocol.Unavailable);
            }
            return;
        }
        if (entries.Count >= maximumResults)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        Retain(entry, work);
        entries.Add(entry);
        observed.Add(entry);
    }

    private void Retain<T>(T value, ReadExecutionBudget work)
    {
        var bytes = work.MeasureResult(value);
        if (bytes > work.MaximumResultBytes - retainedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        retainedBytes += bytes;
    }

    private static bool Same<T>(T left, T right)
        => NativeSerialization.Serialize(left).AsSpan().SequenceEqual(NativeSerialization.Serialize(right));
}
