using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TestInfrastructure;

internal sealed class WholeClassRf3AdmissionEnvironment : IDisposable
{
    internal const string Arguments = "KEYLOAD_TUNIT_LOCAL_RF3_IMAGE_ARGUMENTS";
    internal const string Coverage = "KEYLOAD_TUNIT_NATIVE_COVERAGE_ARGUMENTS";
    internal const string Provenance = "KEYLOAD_IMAGE_PROVENANCE";
    internal const string Receipt = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    internal const string Reference = "KeyLoad__ContainerImages__Server";
    internal const string Child = "KEYLOAD_LOCAL_RF3_IMAGE_CHILD";
    internal const string GithubReceipt = "KEYLOAD_IMAGE_RECEIPT";
    internal const string Revision = "GITHUB_SHA";
    internal const string Github = "GITHUB_ACTIONS";
    internal const string Foreign = "foreign";
    internal static readonly string[] IdentityKeys =
    [
        Coverage, Provenance, Receipt, Reference, Child, GithubReceipt, Revision, Github
    ];
    private readonly Dictionary<string, string?> original;

    internal WholeClassRf3AdmissionEnvironment()
    {
        original = IdentityKeys.Append(Arguments).ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        try
        {
            foreach (var key in original.Keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (var item in original)
        {
            ServerFailureObserver.Observe(() => Environment.SetEnvironmentVariable(item.Key, item.Value), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
