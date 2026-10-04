namespace KeyLoad.UnitTests.Features.Search;

internal static class ThreeWayHybridRrfOracle
{
    private static readonly string[] TextOrder = [ThreeWayHybridTestSupport.A, ThreeWayHybridTestSupport.B,
        ThreeWayHybridTestSupport.C];
    private static readonly string[] VectorOrder = [ThreeWayHybridTestSupport.B, ThreeWayHybridTestSupport.A,
        ThreeWayHybridTestSupport.C, ThreeWayHybridTestSupport.D];
    private static readonly string[] GraphOrder = [ThreeWayHybridTestSupport.D, ThreeWayHybridTestSupport.C,
        ThreeWayHybridTestSupport.B];

    internal static (string Id, double Score)[] Rank(IEnumerable<string> scope, IEnumerable<string> allowed)
    {
        var eligible = scope.Intersect(allowed, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        Add(TextOrder, ThreeWayHybridTestSupport.TextWeight);
        Add(VectorOrder, ThreeWayHybridTestSupport.VectorWeight);
        Add(GraphOrder, ThreeWayHybridTestSupport.GraphWeight);
        return scores.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key, pair.Value)).ToArray();

        void Add(IEnumerable<string> fixedOrder, double weight)
        {
            var rank = 0;
            foreach (var id in fixedOrder.Where(eligible.Contains))
            {
                scores[id] = scores.GetValueOrDefault(id)
                    + weight / (ThreeWayHybridTestSupport.FusionConstant + ++rank);
            }
        }
    }

}
