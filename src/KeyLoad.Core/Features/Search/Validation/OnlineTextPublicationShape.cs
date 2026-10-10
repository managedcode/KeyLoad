namespace KeyLoad.Core.Features.Search;

internal static class OnlineTextPublicationShape
{
    private const string GenerationPrefix = "generation-";
    private const string GuidFormat = "N";
    private const int Sha256HexCharacters = 64;
    private const long EmptyConsumerGeneration = 0;
    private const long EmptySourceCoordinate = 0;
    private const int EmptyTrackedRecords = 0;
    private const int EmptyDataEpoch = 0;
    private const char MinimumDecimalDigit = '0';
    private const char MaximumDecimalDigit = '9';
    private const char MinimumHexLetter = 'a';
    private const char MaximumHexLetter = 'f';

    internal static void Require(OnlineTextPublicationPhaseCommand command, int maximumRecords)
    {
        var request = command.Request;
        var result = command.Result;
        if (command.SessionId == Guid.Empty || command.GenerationId == Guid.Empty || request.CommandId == Guid.Empty
            || result.CommandId != request.CommandId || result.Consumer != request.Consumer
            || result.ConsumerGeneration != request.ConsumerGeneration || request.ConsumerGeneration <= EmptyConsumerGeneration
            || result.TrackedRecords < EmptyTrackedRecords || result.TrackedRecords > maximumRecords
            || command.Leaf != GenerationPrefix + command.GenerationId.ToString(GuidFormat)
            || !IsCanonicalSha(command.ManifestSha256) || !IsCanonicalSha(result.IndexSha256)
            || request.NodeId == Guid.Empty || result.BaseCut.NodeId != request.NodeId
            || result.PublishedCut.NodeId != request.NodeId || command.DataEpoch <= EmptyDataEpoch)
        { throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication); }
        RequireCuts(result.BaseCut, result.PublishedCut);
    }

    private static void RequireCuts(TextIndexSourceCut first, TextIndexSourceCut last)
    {
        if (first.Incarnation == Guid.Empty || first.Incarnation != last.Incarnation
            || first.Position < EmptySourceCoordinate || first.Position > last.Position || first.AppliedPosition < EmptySourceCoordinate
            || first.AppliedPosition > last.AppliedPosition || first.ReadGeneration < EmptySourceCoordinate
            || first.ReadGeneration > last.ReadGeneration || first.ThroughSequence < EmptySourceCoordinate
            || first.ThroughSequence > last.ThroughSequence || first.PolicyEpoch != last.PolicyEpoch
            || first.SchemaVersion != last.SchemaVersion || first.ResourceSha256 != last.ResourceSha256
            || !IsCanonicalSha(first.ResourceSha256))
        { throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication); }
    }

    private static bool IsCanonicalSha(string? value)
        => value is { Length: Sha256HexCharacters }
            && value.All(character => character is >= MinimumDecimalDigit and <= MaximumDecimalDigit
                or >= MinimumHexLetter and <= MaximumHexLetter);
}
