namespace KeyLoad.Features.InternalSerialization;

// Completion is scoped to nullability metadata: a shared collection can be nullable in one
// member and required in another. Cached height also protects a later, deeper shared path.
internal sealed class NativeGraphValidationState
{
    private const int LeafHeight = 0;
    private const int SharedRootDepth = 1;
    private readonly HashSet<object> active = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<NativeGraphKey, int> completed = new(NativeGraphKeyComparer.Instance);
    private int visits;

    internal void Charge()
    {
        // Each serialized occurrence needs a field or JSON token; reuse the public body ceiling
        // as a structural work fence without claiming that it measures decoded heap bytes.
        if (++visits > NativeSerializationLimits.MaximumDomBytes)
        {
            Fail();
        }
    }

    internal bool Enter(object value, bool tracked, NativeValueValidation? validation, int depth, out int height)
    {
        height = LeafHeight;
        RequireDepth(depth);
        if (!tracked)
        {
            return false;
        }
        if (active.Contains(value))
        {
            Fail();
        }
        if (completed.TryGetValue(new(value, validation), out height))
        {
            RequireDepth(checked(depth + height - SharedRootDepth));
            return true;
        }
        _ = active.Add(value);
        return false;
    }

    internal void Complete(object value, bool tracked, NativeValueValidation? validation, int height)
    {
        if (!tracked)
        {
            return;
        }
        completed.Add(new(value, validation), height);
    }

    internal void Leave(object value, bool tracked)
    {
        if (tracked)
        {
            _ = active.Remove(value);
        }
    }

    private static void RequireDepth(int depth)
    {
        if (depth > NativeSerializationLimits.SemanticDepth)
        {
            Fail();
        }
    }

    private static void Fail() => throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
}
