namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ControlledRetireAspireProtocol
{
    internal const int First = 0;
    internal const int Step = 1;
    internal const int InitialCold = 0;
    internal const int ColdCount = 2;
    internal const long InitialCleanup = 0L;
    internal const long NextCleanup = 1L;
    internal const long LastCleanup = 2L;
    internal const int Version = 1;
    internal const int SignatureCharacters = 64;
    internal const int BufferBytes = 8192;
    internal const int Overflow = 1;
    internal const string Unknown = "The write response is unavailable. Retry the same command ID.";
    internal const string HealthyId = "native-retire-healthy";
    internal const string HealthyJson = "{\"retire\":\"healthy\"}";
    internal const string Missing = "The original Aspire Retire operation or cut is absent.";
    internal const string InvalidCut = "The original signed Aspire Retire cut is invalid.";
    internal const string SdkResultKind = "putDocument";
}
