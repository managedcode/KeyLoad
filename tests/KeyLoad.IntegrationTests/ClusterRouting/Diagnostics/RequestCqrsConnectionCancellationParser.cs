using KeyLoad.Clustering;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsConnectionCancellationParser
{
    private const string Prefix = "Native connection original cancellation: ";
    private const string Separator = "; ";
    private const string StageField = "CallSiteStage=";
    private const string ActiveField = "RequestActive=";
    private const string ClosedField = "AdmissionClosed=";
    private const string CancelledField = "OwnerCancellationRequested=";
    private const string TokenField = "OriginalTokenMatchesWriter=";
    private const string GuidFormat = "D";
    private const char Terminator = '.';
    private const int ExactFieldCount = 6;
    private const int RequestField = 0;
    private const int StageOrdinal = 1;
    private const int ActiveOrdinal = 2;
    private const int ClosedOrdinal = 3;
    private const int CancelledOrdinal = 4;
    private const int TokenOrdinal = 5;

    internal static bool TryParse(string line, Guid waveId, string node,
        out RequestCqrsConnectionCancellationRecord record)
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
        var message = line.AsSpan(start + Prefix.Length).TrimEnd("\r\n");
        if (message.IsEmpty || message[^1] != Terminator)
        {
            return false;
        }
        var fields = message[..^1].ToString().Split(Separator, StringSplitOptions.None);
        if (fields.Length != ExactFieldCount
            || !Guid.TryParseExact(fields[RequestField], GuidFormat, out var requestId) || requestId == Guid.Empty
            || !Stage(fields[StageOrdinal], out var stage)
            || !Flag(fields[ActiveOrdinal], ActiveField, out var active)
            || !Flag(fields[ClosedOrdinal], ClosedField, out var closed)
            || !Flag(fields[CancelledOrdinal], CancelledField, out var cancelled)
            || !Flag(fields[TokenOrdinal], TokenField, out var sameToken))
        {
            return false;
        }
        record = new(waveId, node, requestId, stage, active, closed, cancelled, sameToken);
        return true;
    }

    private static bool Stage(string field, out GrainFailureStage stage)
    {
        stage = default;
        if (!field.StartsWith(StageField, StringComparison.Ordinal))
        {
            return false;
        }
        var value = field.AsSpan(StageField.Length);
        return Enum.TryParse(value, false, out stage) && Enum.IsDefined(stage)
            && value.SequenceEqual(stage.ToString());
    }

    private static bool Flag(string field, string prefix, out bool value)
    {
        value = default;
        if (!field.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }
        var literal = field.AsSpan(prefix.Length);
        return bool.TryParse(literal, out value) && literal.SequenceEqual(value.ToString());
    }
}
