namespace KeyLoad.Storage.ZoneTree;

/// <summary>Constant-state verification of every owner chain against its retained immutable source ancestry.</summary>
internal sealed class ZoneTreeConservationLineage
{
    private const long Empty = 0;
    private const string Invalid = "The native retirement image lineage is incomplete or divergent.";
    private readonly ResourceRetirementConservationFloorV1? floor;
    private readonly ResourceRetirementConservationFloorV1? required;
    private readonly Func<byte[], byte[]?> read;
    private readonly int maximumBytes;
    private readonly ResourceRetirementAuthorityAccumulator expectedOwners = new();
    private readonly ResourceRetirementAuthorityAccumulator actualOwners = new();
    private readonly ResourceRetirementAuthorityAccumulator expectedMarkers = new();
    private readonly ResourceRetirementAuthorityAccumulator actualMarkers = new();
    private ResourceRetirementConservationFloorV1? current;
    private ResourceRetirementConservationFloorV1? anchor;
    private bool matched;

    internal ZoneTreeConservationLineage(ResourceRetirementConservationFloorV1? floor,
        ResourceRetirementConservationFloorV1? required, Func<byte[], byte[]?> read, int maximumBytes)
    {
        this.floor = floor;
        this.required = required;
        this.read = read;
        this.maximumBytes = maximumBytes;
        matched = required is null;
        RequireAcyclic();
        for (var ancestor = floor; ancestor is not null; ancestor = Parent(ancestor))
        {
            ResourceRetirementConservation.Validate(ancestor);
            if (ancestor.Revision > Empty)
            {
                AddOwner(expectedOwners, ancestor);
            }
            var baseline = Baseline(ancestor);
            matched |= required is not null && ResourceRetirementConservation.Same(baseline, required);
        }
    }

    internal void Step(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        ResourceRetirementAuthorityClassifier.RequireBound(key, value, maximumBytes);
        var step = NativeSerialization.Deserialize<ResourceRetirementConservationStepV1>(value);
        ResourceRetirementConservation.ValidateStep(step);
        if (!key.SequenceEqual(ResourceRetirementConservationKeys.Step(step.PhysicalOwner, step.Revision)))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        if (current is null || !ResourceRetirementConservation.SameOwner(current.PhysicalOwner, step.PhysicalOwner))
        {
            FinishOwner();
            anchor = Find(step.PhysicalOwner);
            current = Baseline(anchor);
        }
        current = ResourceRetirementConservation.Advance(current, step);
        matched |= required is not null && ResourceRetirementConservation.Same(current, required);
        ResourceRetirementConservationMarkers.AppendExpected(expectedMarkers, step);
    }

    internal void Marker(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
        => ResourceRetirementConservationMarkers.AppendActual(actualMarkers, key, value, maximumBytes);

    internal void Complete()
    {
        FinishOwner();
        if (!matched || !expectedOwners.Complete().Same(actualOwners.Complete())
            || !expectedMarkers.Complete().Same(actualMarkers.Complete()))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }

    private void FinishOwner()
    {
        if (current is null)
        {
            return;
        }
        if (anchor is null || !ResourceRetirementConservation.Same(current, anchor))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        AddOwner(actualOwners, anchor);
        current = null;
    }

    private void RequireAcyclic()
    {
        var slow = floor;
        var fast = floor;
        while (fast is not null)
        {
            fast = Parent(fast);
            if (fast is null)
            {
                return;
            }
            fast = Parent(fast);
            slow = slow is null ? null : Parent(slow);
            if (fast is not null && slow is not null
                && ResourceRetirementConservation.SameOwner(fast.PhysicalOwner, slow.PhysicalOwner))
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
        }
    }

    private ResourceRetirementConservationFloorV1 Find(PhysicalShardRecord owner)
    {
        for (var ancestor = floor; ancestor is not null; ancestor = Parent(ancestor))
        {
            if (ResourceRetirementConservation.SameOwner(ancestor.PhysicalOwner, owner))
            {
                return ancestor;
            }
        }
        throw Errors.Fail(ErrorCode.Corruption, Invalid);
    }

    private ResourceRetirementConservationFloorV1 Baseline(ResourceRetirementConservationFloorV1 selected)
    {
        if (selected.BaselineOriginKey.IsEmpty)
        {
            return ResourceRetirementConservation.Initial(selected.PhysicalOwner);
        }
        var key = selected.BaselineOriginKey.ToArray();
        var value = read(key) ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        return ResourceRetirementConservationBaseline.FromOrigin(selected, key, value, maximumBytes);
    }

    private ResourceRetirementConservationFloorV1? Parent(ResourceRetirementConservationFloorV1 selected)
    {
        if (selected.BaselineOriginKey.IsEmpty)
        {
            return null;
        }
        var key = selected.BaselineOriginKey.ToArray();
        var value = read(key) ?? throw Errors.Fail(ErrorCode.Corruption, Invalid);
        ResourceRetirementConservationBaseline.FromOrigin(selected, key, value, maximumBytes);
        return NativeSerialization.Deserialize<ResourceRetirementConservationSourceOriginV1>(value).SourceFloor;
    }

    private static void AddOwner(ResourceRetirementAuthorityAccumulator accumulator,
        ResourceRetirementConservationFloorV1 selected)
        => accumulator.Add(ResourceRetirementConservationKeys.StepOwnerPrefix(selected.PhysicalOwner),
            NativeSerialization.Serialize(selected));
}
