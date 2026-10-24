using KeyLoad.Query;

namespace KeyLoad.UnitTests.InternalSerialization;

internal static class GraphFixtures
{
    internal const string NodeAlias = "keyload.tests.execution-graph.node";
    internal const string BranchAlias = "keyload.tests.execution-graph.branches";
    internal const string CollectionsAlias = "keyload.tests.execution-graph.collections";
    private const string Field = "amount";
    private const string Operator = "AND";

    internal static Logical SharedPredicate(int levels)
    {
        Predicate value = new NullTest(new FieldOperand(Field), Negated: false, Missing: false);
        for (var level = 0; level < levels; level++)
        {
            value = new Logical(value, Operator, value);
        }
        return (Logical)value;
    }

    internal static GraphNode Chain(int nodes, GraphNode? tail = null)
    {
        for (var index = 0; index < nodes; index++)
        {
            tail = new()
            {
                Next = tail
            };
        }
        return tail!;
    }

    internal static byte[] EncodeUnchecked<T>(T value)
        => OrleansSerialization.Serializer.SerializeToArray(
            new RuntimePayload { Version = PayloadVersion.Current, Value = value });
}
