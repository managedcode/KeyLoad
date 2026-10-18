namespace KeyLoad.Comparisons.Targets;

internal static class OpenSearchVectorScripts
{
    internal const string Initialize = $$"""
        state.query = new double[params.{{OpenSearchNames.QueryValue}}.size()];
        state.norm = 0.0;
        state.candidates = new ArrayList();
        for (int i = 0; i < state.query.length; i++) {
            double value = (double)(float)params.{{OpenSearchNames.QueryValue}}[i];
            state.query[i] = value;
            state.norm += value * value;
        }
        if (!Double.isFinite((double)state.norm) || state.norm <= 0.0) {
            throw new IllegalArgumentException('{{OpenSearchNames.VectorInputMismatch}}');
        }
        """;

    private const string Rank = $$"""
        int insertionPosition(List candidates, double score, String id) {
            for (int position = 0; position < candidates.size(); position++) {
                def previous = candidates[position];
                double previousScore = (double)previous.{{OpenSearchNames.VectorScore}};
                if (score > previousScore || (score == previousScore && id.compareTo((String)previous.{{OpenSearchNames.Id}}) < 0)) {
                    return position;
                }
            }
            return candidates.size();
        }
        """;

    internal const string Map = Rank + $$"""

        float[] vector = doc['{{OpenSearchNames.Vector}}'].value;
        double[] query = state.query;
        if (vector.length != query.length) {
            throw new IllegalArgumentException('{{OpenSearchNames.VectorInputMismatch}}');
        }
        double dot = 0.0;
        double norm = 0.0;
        for (int i = 0; i < vector.length; i++) {
            double value = (double)vector[i];
            dot += query[i] * value;
            norm += value * value;
        }
        double score = dot / Math.sqrt((double)state.norm * norm);
        if (!Double.isFinite(score)) {
            throw new IllegalArgumentException('{{OpenSearchNames.VectorInputMismatch}}');
        }
        String id = doc['{{OpenSearchNames.IdKeyword}}'].value;
        int position = insertionPosition(state.candidates, score, id);
        if (position < params.{{OpenSearchNames.VectorTopK}}) {
            def source = params.{{OpenSearchNames.Source}};
            if (!id.equals(source.{{OpenSearchNames.Id}}) || !(source.{{OpenSearchNames.Payload}} instanceof Map)) {
                throw new IllegalArgumentException('{{OpenSearchNames.VectorResponseMismatch}}');
            }
            if (state.candidates.size() == params.{{OpenSearchNames.VectorTopK}}) {
                state.candidates.remove(state.candidates.size() - 1);
            }
            state.candidates.add(position, ['{{OpenSearchNames.Id}}': id,
                '{{OpenSearchNames.Payload}}': source.{{OpenSearchNames.Payload}}, '{{OpenSearchNames.VectorScore}}': score]);
        }
        """;

    internal const string Combine = "return state.candidates;";

    internal const string Reduce = Rank + $$"""

        def candidates = new ArrayList();
        for (def shard : states) {
            if (shard == null) {
                continue;
            }
            if (shard.size() > params.{{OpenSearchNames.VectorTopK}}) {
                throw new IllegalArgumentException('{{OpenSearchNames.VectorResponseMismatch}}');
            }
            for (def candidate : shard) {
                double score = (double)candidate.{{OpenSearchNames.VectorScore}};
                if (!Double.isFinite(score)) {
                    throw new IllegalArgumentException('{{OpenSearchNames.VectorResponseMismatch}}');
                }
                String id = candidate.{{OpenSearchNames.Id}};
                int position = insertionPosition(candidates, score, id);
                if (position < params.{{OpenSearchNames.VectorTopK}}) {
                    if (candidates.size() == params.{{OpenSearchNames.VectorTopK}}) {
                        candidates.remove(candidates.size() - 1);
                    }
                    candidates.add(position, candidate);
                }
            }
        }
        return candidates;
        """;
}
