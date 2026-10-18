namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Independent ordinal-key golden for every public field of the CLUSTER link fixture.</summary>
internal static class DatabaseCompositionClusterPayload
{
    internal static string OrdinalGolden(QueueGraphLink link)
        => FunctionalGateBoundJson.Serialize(link);
}
