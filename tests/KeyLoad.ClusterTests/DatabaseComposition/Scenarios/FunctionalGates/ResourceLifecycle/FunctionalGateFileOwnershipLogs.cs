using System.Text;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateFileOwnershipProtocol;

namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Retains the original bounded execution refusal prefix while separating batch traversal from stream ownership.</summary>
internal static class FunctionalGateFileOwnershipLogs
{
    internal static async Task<string> ReadAsync(FunctionalGateFixture fixture, string name, CancellationToken token)
    {
        var profile = fixture.Identity;
        string[] secrets = [profile.AdminKey, profile.SigningKey];
        var output = new StringBuilder();
        var logs = fixture.App.Services.GetRequiredService<ResourceLoggerService>();
        // DCP writes each project log under its suffixed instance name; the resource overload resolves that name.
        var resource = fixture.App.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ProjectResource>().Single(value => value.Name == name);
        try
        {
            await foreach (var batch in logs.WatchAsync(resource).WithCancellation(token).ConfigureAwait(false))
            {
                var refusal = (ReadBatch(batch, secrets, output)).Value;
                if (refusal is not null)
                {
                    return refusal;
                }
            }
        }
        catch (Exception failure)
        {
            failure.Data[P.LogEvidenceKey] = output.ToString();
            throw;
        }
        throw new InvalidDataException(P.Incomplete);
    }

    private static (bool HasValue, string? Value) ReadBatch(IEnumerable<LogLine> batch, string[] secrets, StringBuilder output)
    {
        foreach (var line in batch)
        {
            var text = line.Content;
            foreach (var secret in secrets.Where(secret => !string.IsNullOrEmpty(secret)))
            {
                text = text.Replace(secret, P.Redacted, StringComparison.Ordinal);
            }
            if (text.Length + P.LogLineEnding.Length > P.MaximumLogCharacters - output.Length)
            {
                var failure = new InvalidDataException(P.Incomplete);
                failure.Data[P.LogEvidenceKey] = output.ToString();
                throw failure;
            }
            output.Append(text).Append(P.LogLineEndingCharacter);
            if (FunctionalGateFileOwnershipAssertions.HasOriginalRefusal(output.ToString()))
            {
                return (true, ((string?)(output.ToString())));
            }
        }
        return (false, default(string?));
    }
}
