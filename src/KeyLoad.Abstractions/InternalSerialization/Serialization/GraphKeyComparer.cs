using System.Runtime.CompilerServices;

namespace KeyLoad.InternalSerialization;

// Record payloads can have expensive structural equality. Memoization always compares the
// payload by identity while retaining the applicable collection-nullability contract.
internal sealed class GraphKeyComparer : IEqualityComparer<GraphKey>
{
    internal static readonly GraphKeyComparer Instance = new();

    public bool Equals(GraphKey left, GraphKey right)
        => ReferenceEquals(left.Value /* KLD0041: This is the retained left graph-node reference, whose runtime class is intentionally heterogeneous. */, right.Value /* KLD0041: This is the retained right graph-node reference, compared without structural record equality. */) /* KLD0041: Memoized graph identity requires the CLR reference-equality operation across different record classes. */
            && EqualityComparer<ValueValidation?>.Default.Equals(left.Validation, right.Validation);

    public int GetHashCode(GraphKey value)
        => HashCode.Combine(RuntimeHelpers.GetHashCode(value.Value /* KLD0041: The memoized node is an existing tracked reference, never a newly boxed scalar. */) /* KLD0041: The CLR identity hash must agree with reference equality rather than a record override. */, value.Validation);
}
