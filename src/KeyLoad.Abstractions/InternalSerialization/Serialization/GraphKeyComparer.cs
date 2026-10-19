using System.Runtime.CompilerServices;

namespace KeyLoad.Features.InternalSerialization;

// Record payloads can have expensive structural equality. Memoization always compares the
// payload by identity while retaining the applicable collection-nullability contract.
internal sealed class GraphKeyComparer : IEqualityComparer<GraphKey>
{
    internal static readonly GraphKeyComparer Instance = new();

    public bool Equals(GraphKey left, GraphKey right)
        => ReferenceEquals(left.Value, right.Value)
            && EqualityComparer<ValueValidation?>.Default.Equals(left.Validation, right.Validation);

    public int GetHashCode(GraphKey value)
        => HashCode.Combine(RuntimeHelpers.GetHashCode(value.Value), value.Validation);
}
