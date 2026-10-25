using KeyLoad.Server;

namespace KeyLoad.ClusterTests.OrleansExecution;

internal static class RequestCqrsRpcFailureParser
{
    private const string Prefix = "Orleans request stream failed: ";
    private const string CategoryStart = " (";
    private const string ErrorStart = ") with ";
    private const string StageStart = "; Stage=";
    private const string KindStart = "; Kind=";
    private const string GuidFormat = "D";
    private const char Terminator = '.';
    internal static bool TryParse(string line, Guid waveId, string node, out RequestCqrsRpcFailureRecord record)
    {
        record = null!;
        if (line.Length > RequestCqrsClusterMcpRejectionNodeCapture.MaximumLineCharacters)
        {
            return false;
        }
        var start = line.IndexOf(Prefix, StringComparison.Ordinal);
        if (start < 0 || line.IndexOf(Prefix, start + Prefix.Length, StringComparison.Ordinal) >= 0)
        {
            return false;
        }
        var text = line.AsSpan(start + Prefix.Length).TrimEnd("\r\n");
        var category = text.IndexOf(CategoryStart, StringComparison.Ordinal);
        var error = text.IndexOf(ErrorStart, StringComparison.Ordinal);
        if (category <= 0 || error <= category || text[^1] != Terminator
            || !Guid.TryParseExact(text[..category], GuidFormat, out var requestId) || requestId == Guid.Empty)
        {
            return false;
        }
        var categoryText = text[(category + CategoryStart.Length)..error];
        var tail = text[(error + ErrorStart.Length)..^1];
        var stageMarker = tail.IndexOf(StageStart, StringComparison.Ordinal);
        var kindMarker = tail.IndexOf(KindStart, StringComparison.Ordinal);
        if (stageMarker <= 0 || kindMarker <= stageMarker + StageStart.Length
            || !TryReadOrigin(tail, stageMarker, kindMarker, out var stage, out var failureKind))
        {
            return false;
        }
        var errorText = tail[..stageMarker];
        if (!Enum.TryParse<OrleansRpcFailureCategory>(categoryText, false, out var kind) || !Enum.IsDefined(kind)
            || !categoryText.SequenceEqual(kind.ToString())
            || !Enum.TryParse<ErrorCode>(errorText, false, out var code)
            || code is not (ErrorCode.OwnershipLost or ErrorCode.UnknownWriteOutcome)
            || !errorText.SequenceEqual(code.ToString()))
        {
            return false;
        }
        record = new(waveId, node, requestId, kind, code, stage, failureKind);
        return true;
    }

    private static bool TryReadOrigin(ReadOnlySpan<char> tail, int stageMarker, int kindMarker,
        out OrleansRpcFailureStage stage, out OrleansRpcFailureKind kind)
    {
        var stageText = tail[(stageMarker + StageStart.Length)..kindMarker];
        var kindText = tail[(kindMarker + KindStart.Length)..];
        stage = default;
        kind = default;
        return Enum.TryParse(stageText, false, out stage) && Enum.IsDefined(stage)
            && stageText.SequenceEqual(stage.ToString())
            && Enum.TryParse(kindText, false, out kind) && Enum.IsDefined(kind)
            && kindText.SequenceEqual(kind.ToString());
    }
}
