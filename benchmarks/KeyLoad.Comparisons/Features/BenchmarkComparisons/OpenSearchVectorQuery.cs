using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchVectorQuery
{
    private static readonly string[] SourceFields = [OpenSearchNames.Id, OpenSearchNames.Payload];

    internal static object Create(ImmutableArray<float> vector, int topK)
        => new
        {
            size = topK,
            _source = SourceFields,
            query = new
            {
                script_score = new
                {
                    query = new Dictionary<string, object>
                    {
                        [OpenSearchNames.Bool] = new Dictionary<string, object>
                        {
                            [OpenSearchNames.Filter] = new Dictionary<string, object>
                            {
                                [OpenSearchNames.Exists] = new Dictionary<string, object>
                                {
                                    [OpenSearchNames.Field] = OpenSearchNames.Vector
                                }
                            }
                        }
                    },
                    script = new
                    {
                        source = OpenSearchNames.KnnScore,
                        lang = OpenSearchNames.KnnLanguage,
                        @params = new Dictionary<string, object>
                        {
                            [OpenSearchNames.Field] = OpenSearchNames.Vector,
                            [OpenSearchNames.QueryValue] = vector,
                            [OpenSearchNames.SpaceType] = OpenSearchNames.CosineSimilarity
                        }
                    }
                }
            },
            sort = new object[]
            {
                new Dictionary<string, object> { [OpenSearchNames.Score] = new { order = OpenSearchNames.Descending } },
                new Dictionary<string, object> { [OpenSearchNames.IdKeyword] = new { order = OpenSearchNames.Ascending } }
            }
        };
}
