using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchVectorQuery
{
    internal static object Create(ImmutableArray<float> vector, int topK)
    {
        const int NoObservedItems = 0;

        if (vector.IsDefault || vector.Length is < OpenSearchNames.MinimumVectorDimensions or > OpenSearchNames.MaximumVectorDimensions
            || vector.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentOutOfRangeException(nameof(vector));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, OpenSearchNames.MinimumTopK);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(topK, OpenSearchNames.MaximumTopK);

        return new
        {
            size = NoObservedItems,
            query = new Dictionary<string, object>
            {
                [OpenSearchNames.Exists] = new Dictionary<string, object> { [OpenSearchNames.Field] = OpenSearchNames.Vector }
            },
            aggregations = new Dictionary<string, object>
            {
                [OpenSearchNames.ExactNeighborsAggregation] = new Dictionary<string, object>
                {
                    [OpenSearchNames.ScriptedMetric] = new Dictionary<string, object>
                    {
                        [OpenSearchNames.ScriptParameters] = new Dictionary<string, object>
                        {
                            [OpenSearchNames.QueryValue] = vector,
                            [OpenSearchNames.VectorTopK] = topK
                        },
                        [OpenSearchNames.InitScript] = Script(OpenSearchVectorScripts.Initialize),
                        [OpenSearchNames.MapScript] = Script(OpenSearchVectorScripts.Map),
                        [OpenSearchNames.CombineScript] = Script(OpenSearchVectorScripts.Combine),
                        [OpenSearchNames.ReduceScript] = Script(OpenSearchVectorScripts.Reduce)
                    }
                }
            }
        };
    }

    private static object Script(string source) => new { lang = OpenSearchNames.PainlessLanguage, source };
}
