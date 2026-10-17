namespace KeyLoad.Query.Features.Search;

internal interface IPackedAnnSimilarity
{
    double ScorePacked(PackedAnnVectors vectors, int ordinal);
}
