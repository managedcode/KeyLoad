namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Independent ordinal-key golden for every public field of the RF3 link fixture.</summary>
internal static class DatabaseCompositionRf3Payload
{
    internal static string OrdinalGolden(QueueGraphLink link)
        => FunctionalGateBoundJson.Serialize(link);
}
