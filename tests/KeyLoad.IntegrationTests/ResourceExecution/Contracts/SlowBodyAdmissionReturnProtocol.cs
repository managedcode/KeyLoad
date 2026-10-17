namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class SlowBodyAdmissionReturnProtocol
{
    internal const long MaximumBytes = 130_000;
    internal const int BodyBytes = 8_192;
    internal const int ControlBodyBytes = 1_024;
    internal const long ControlBytes = 20_000;
    internal const int OneConnection = 1;
    internal const int FirstByte = 1;
    internal const string ContentType = "application/json";
    internal const string Bearer = "Bearer";
}
