namespace KeyLoad.InternalSerialization;

// Completion is scoped to nullability metadata: a shared collection can be nullable in one
// member and required in another. Cached height also protects a later, deeper shared path.
internal sealed class GraphValidationState
{
    private readonly HashSet<object> /* KLD0041: Active traversal nodes are heterogeneous reference identities; scalar values never enter this set. */ active = new(ReferenceEqualityComparer.Instance) /* KLD0041: The native reference comparer prevents structural record equality during cycle detection. */;
    private readonly Dictionary<GraphKey, int> completed = new(GraphKeyComparer.Instance);
    private int visits;

    internal void Charge()
    {
        // Each serialized occurrence needs a field or JSON token; reuse the public body ceiling
        // as a structural work fence without claiming that it measures decoded heap bytes.
        if (++visits > SerializationLimits.MaximumDomBytes)
        {
            Fail();
        }
    }

    internal bool Enter(object /* KLD0041: This parameter is the current runtime graph node; the tracked flag admits only reference identities to the set. */ value, bool tracked, ValueValidation? validation, int depth, out int height)
    {
        height = 0;
        RequireDepth(depth);
        if (!tracked)
        {
            return false;
        }
        if (active /* KLD0041: This read selects the active reference-identity set for the current graph traversal. */.Contains(value) /* KLD0041: Cycle detection compares the existing runtime reference without inspecting or copying its payload. */)
        {
            Fail();
        }
        if (completed.TryGetValue(new(value, validation) /* KLD0041: The graph key retains only an existing tracked reference and its validation policy, using reference identity. */, out height))
        {
            RequireDepth(checked(depth + height - 1));
            return true;
        }
        _ = (active /* KLD0041: This write targets the active reference-identity set for the current traversal path. */.Add(value) /* KLD0041: Only a tracked existing reference is retained until its recursive traversal finishes. */);
        return false;
    }

    internal void Complete(object /* KLD0041: Completed-height memoization keys the existing tracked runtime reference and its nullability metadata. */ value, bool tracked, ValueValidation? validation, int height)
    {
        if (!tracked)
        {
            return;
        }
        completed.Add(new(value, validation) /* KLD0041: The graph key retains only an existing tracked reference and its validation policy, using reference identity. */, height);
    }

    internal void Leave(object /* KLD0041: The completed traversal removes this same tracked runtime reference from its active path. */ value, bool tracked)
    {
        if (tracked)
        {
            _ = (active /* KLD0041: This read selects the active path identities joined by the current recursive traversal. */.Remove(value) /* KLD0041: Teardown removes the exact tracked reference that entered this path, without structural equality. */);
        }
    }

    private static void RequireDepth(int depth)
    {
        if (depth > SerializationLimits.SemanticDepth)
        {
            Fail();
        }
    }

    private static void Fail() => throw Errors.Fail(ErrorCode.Corruption, PayloadVersion.InvalidPayload);
}
