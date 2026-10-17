namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>Classifies only fixed native failure fields for the original wrong-owner assertion.</summary>
internal static class NativeTextMaintenanceFailureDiagnostic
{
    private const string InvalidRequest = "The internal request has an invalid scope, key, expiry or operation.";
    private const string ServerResponseUnavailable = "The server response is unavailable.";
    private const string WriteResponseUnavailable = "The write response is unavailable. Retry the same command ID.";
    private const string OrleansCommandInterrupted = "The write outcome is unknown. Retry the same command ID.";
    private const string Absent = "Absent";
    private const string Other = "Other";
    private const string MessagePrefix = "Static native maintenance failure code=";
    private const string DetailSeparator = " detailCategory=";
    private const string MessageSuffix = ".";
    private const string TextMaintenanceInterrupted = nameof(TextMaintenanceInterrupted);

    internal static string Describe(string? errorCode, string? detail)
    {
        var code = errorCode is null ? Absent : Enum.TryParse<ErrorCode>(errorCode, out var value)
            && Enum.IsDefined(value) && value.ToString() == errorCode ? value.ToString() : Other;
        var category = detail switch
        {
            null => Absent,
            InvalidRequest => nameof(InvalidRequest),
            ServerResponseUnavailable => nameof(ServerResponseUnavailable),
            WriteResponseUnavailable => nameof(WriteResponseUnavailable),
            OrleansCommandInterrupted => nameof(OrleansCommandInterrupted),
            TextIndexMaintenanceProtocol.Interrupted => nameof(TextMaintenanceInterrupted),
            _ => Other
        };
        return string.Concat(MessagePrefix, code, DetailSeparator, category, MessageSuffix);
    }
}
