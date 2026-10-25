using System.Collections.Immutable;
using KeyLoad.Query.Search;
using static KeyLoad.FunctionalTests.Search.AnnProviderQualificationProtocol;

namespace KeyLoad.FunctionalTests.Search;

internal static class AnnProviderQualificationCorpus
{
    internal static readonly float[] Query = [PositiveComponent, EmptyComponent];
    internal static VectorSpace Space(DistanceMetric metric) => new(SpaceId, Dimension, metric, Model, ModelVersion);

    internal static VectorRecord[] Expected(AnnProviderQualificationState state)
    {
        var space = Space(state.Metric);
        var rows = new List<VectorRecord>
        {
            new(Alpha, Field, space, [PositiveComponent, EmptyComponent], state.Healthy ? HealthyRevision : InitialRevision)
        };
        if (!state.Deleted || state.Reinserted)
        {
            rows.Add(new(Beta, Field, space, state.Reinserted
                ? [PositiveComponent, PositiveComponent] : [EmptyComponent, PositiveComponent],
                state.Reinserted ? ReinsertedRevision : InitialRevision));
        }
        rows.Add(new(Gamma, Field, space, [NegativeComponent, EmptyComponent], InitialRevision));
        return rows.ToArray();
    }

    internal static ImmutableArray<Mutation> Put(AnnProviderQualificationState state, string id,
        ImmutableArray<float> values, long expectedDocumentRevision, long vectorRevision)
        => [new PutDocument(Collection, id, DocumentJson, expectedDocumentRevision),
            new PutVector(Collection, id, Field, values, Space(state.Metric), vectorRevision)];

    internal static VectorRecord[] Read(AnnProviderQualificationOwner owner)
        => owner.Database.WithVectors(Root, owner.Source.Partition, Collection, Field,
            (_, _, pairs) => pairs.Select(pair => pair.Vector).OrderBy(row => row.DocumentId, StringComparer.Ordinal).ToArray());

    internal static PackedAnnIndex Build(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        CancellationToken token)
        => PackedAnnIndex.Build(Space(state.Metric), Read(owner), UnitExecutionOptions.PackedAnn(state.Policy), Budget(owner, token));

    internal static AnnWorkBudget Budget(AnnProviderQualificationOwner owner, CancellationToken token)
        => new(new KeyLoad.Core.ReadExecutionBudget(
            owner.Database.EvaluationClock, token), PackedAnnIndexTestSupport.GenerousWorkLimit);

    internal static AnnCandidate[] Candidates(AnnProviderQualificationState state)
    {
        var rows = Expected(state);
        return rows.Select((row, ordinal) => new AnnCandidate(ordinal, row.DocumentId, row.DocumentRevision,
            Score(state.Metric, row.DocumentId, state.Reinserted))).OrderByDescending(row => row.Score)
            .ThenBy(row => row.DocumentId, StringComparer.Ordinal).ToArray();
    }

    private static double Score(DistanceMetric metric, string id, bool reinserted)
    {
        if (id == Alpha)
        {
            return metric == DistanceMetric.Euclidean ? ZeroScore : UnitScore;
        }
        if (id == Gamma)
        {
            return metric == DistanceMetric.Euclidean ? -OppositeVectorDistance : OppositeScore;
        }
        return metric switch
        {
            DistanceMetric.DotProduct => reinserted ? UnitScore : ZeroScore,
            DistanceMetric.Cosine => reinserted ? UnitScore / Math.Sqrt(ReinsertedSquaredNorm) : ZeroScore,
            DistanceMetric.Euclidean => reinserted ? OppositeScore : -Math.Sqrt(OrthogonalSquaredDistance),
            _ => throw new InvalidOperationException(InvalidDamage)
        };
    }
}
