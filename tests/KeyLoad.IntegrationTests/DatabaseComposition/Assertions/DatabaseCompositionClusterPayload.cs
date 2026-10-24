namespace KeyLoad.IntegrationTests.DatabaseComposition;

/// <summary>Independent ordinal-key golden for every public field of the Orleans link fixture.</summary>
internal static class DatabaseCompositionClusterPayload
{
    internal static string OrdinalGolden(QueueGraphLink link)
        => FunctionalGateBoundJson.Serialize(link);
}
