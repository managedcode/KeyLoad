using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterRouting;

[ConfigurationBinding]
internal static class MovementFrameObservationSettingsReader
{
    private const int MaximumSettings = 3;
    private const int ExcessSetting = 1;
    private const int NoSettings = 0;
    private const int FirstNestedSetting = 1;
    private const string Enabled = "Enabled";
    private const string Root = "Root";
    private const string SessionId = "SessionId";
    private const string SessionFormat = "N";

    internal static MovementFrameObservationProfileSettings? Read(IConfiguration configuration, AppHostControlOptions control)
    {
        var section = configuration.GetSection(MovementFrameObservationProfileProtocol.Section);
        var fields = section.GetChildren().Take(MaximumSettings + ExcessSetting).ToArray();
        if (fields.Length == NoSettings && section.Value is null)
        { return null; }
        if (section.Value is not null || fields.Length > MaximumSettings
            || fields.Any(field => field.Key is not (Enabled or Root or SessionId)
                || field.Value is null || field.GetChildren().Take(FirstNestedSetting).Any())
            || !bool.TryParse(section[Enabled], out var enabled))
        { throw Invalid(); }
        if (!enabled)
        {
            if (fields.Any(field => field.Key != Enabled))
            { throw Invalid(); }
            return null;
        }
        var root = section[Root];
        var session = section[SessionId];
        if (string.IsNullOrWhiteSpace(root) || !Guid.TryParseExact(session, SessionFormat, out var identity)
            || identity == Guid.Empty || identity.ToString(SessionFormat) != session
            || !control.Ephemeral || !control.TwoRf3 || !control.ProtectedDocumentMovement
            || !control.RemoteDocumentReads || !control.RemotePartitionQueries || control.MovementMaxFrameBytes is null
            || control.Tests is not null || control.BenchmarksEnabled || control.ComparisonSelectorsPresent
            || control.TargetSelected || control.ScaleSelected || control.ProtocolCohortEnabled
            || control.ProtocolCohortConfigured || control.LoggerModelControl)
        { throw Invalid(); }
        return new(root, session!);
    }
    private static InvalidOperationException Invalid() => new(MovementFrameObservationProfileProtocol.Invalid);
}
