namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class ConnectionActivationCountWitness
{
    internal static void Write(string kind, int expected, int actual, bool primaryOwnerPresent,
        bool lastClosedOwnerPresent, bool lastClosedOwnerIsPrimary, bool primarySignedCloseJoined)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            Kind = kind,
            Expected = expected,
            Actual = actual,
            PrimaryOwnerPresent = primaryOwnerPresent,
            LastClosedOwnerPresent = lastClosedOwnerPresent,
            LastClosedOwnerIsPrimary = lastClosedOwnerIsPrimary,
            PrimarySignedCloseJoined = primarySignedCloseJoined
        }));
    }
}
