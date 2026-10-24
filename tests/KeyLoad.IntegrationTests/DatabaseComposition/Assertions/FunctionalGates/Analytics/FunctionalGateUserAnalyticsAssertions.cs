using System.Text.Json;
using KeyLoad.IntegrationTests.StorageReplication;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateUserAnalyticsAssertions
{
    private const int MaximumDiagnosticCharacters = 2_048;

    internal static async Task EqualAsync<T>(T actual, T expected)
    {
        var observed = JsonSerializer.SerializeToElement(actual, JsonDefaults.Options);
        var required = JsonSerializer.SerializeToElement(expected, JsonDefaults.Options);
        // Map member order is not public state. Every value/member and array position remains compared.
        await Assert.That(JsonElement.DeepEquals(observed, required)).IsTrue()
            .Because($"actual={Bounded(observed)} expected={Bounded(required)}");
        if (actual is CommitReceipt actualReceipt && expected is CommitReceipt expectedReceipt)
        {
            await SubmissionColdAssertions.ReceiptAsync(actualReceipt, expectedReceipt);
        }
    }

    private static string Bounded(JsonElement value)
    {
        var json = value.GetRawText();
        return json[..Math.Min(json.Length, MaximumDiagnosticCharacters)];
    }
}
