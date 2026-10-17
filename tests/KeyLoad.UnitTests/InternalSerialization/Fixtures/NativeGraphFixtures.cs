using KeyLoad.Features.InternalSerialization;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class NativeGraphFixtures
{
    internal const string NodeAlias = "keyload.tests.native-graph.node.v1";
    internal const string BranchAlias = "keyload.tests.native-graph.branches.v1";
    internal const string CollectionsAlias = "keyload.tests.native-graph.collections.v1";
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

    internal static NativeGraphNode Chain(int nodes, NativeGraphNode? tail = null)
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
        => NativeSerializerProviders.Get(typeof(T)).Serializer.SerializeToArray(
            new NativePayload { Version = NativePayloadVersion.Current, Value = value });
}
