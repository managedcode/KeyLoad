using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Bounds the isolated KeyLoad comparison's observed HTTP admission profile.</summary>
[ConfigurationOptions]
public sealed class IsolatedKeyLoadAdmissionOptions
{
    private const int MinimumRequests = 1;
    private const long MinimumReservedBytes = 1;
    private const int DefaultRequestsPerScope = 32;
    private const int DocumentRequestsPerScope = 512;
    private const long DefaultReservedBytes = 2_147_483_648;
    /// <summary>The isolated scenario configuration section.</summary>
    public const string SectionName = "IsolatedKeyLoadAdmission";
    /// <summary>The invalid isolated admission configuration diagnostic.</summary>
    public const string ValidationMessage = "Isolated KeyLoad admission settings are invalid.";
    /// <summary>The request slots for each node, tenant, principal and reserved control scope.</summary>
    public int RequestsPerScope { get; set; } = DefaultRequestsPerScope;
    /// <summary>Enables only the declared document-family request capacity.</summary>
    public bool DocumentWorkload { get; set; }
    /// <summary>The total bytes reserved by data requests on one node.</summary>
    public long ReservedBytes { get; set; } = DefaultReservedBytes;

    /// <summary>Checks the existing qualified scenario ceilings.</summary>
    /// <returns>Whether the complete settings are supported.</returns>
    public bool IsValid() => (DocumentWorkload ? RequestsPerScope == DocumentRequestsPerScope
        : RequestsPerScope is >= MinimumRequests and <= DefaultRequestsPerScope)
        && ReservedBytes is >= MinimumReservedBytes and <= DefaultReservedBytes;

    /// <summary>Rejects invalid settings before a node or observer is started.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new OptionsValidationException(SectionName, typeof(IsolatedKeyLoadAdmissionOptions), [ValidationMessage]);
        }
    }

    /// <summary>Derives the exact HTTP admission options from the bound scenario policy.</summary>
    /// <param name="options">The validated isolated scenario policy.</param>
    /// <returns>The validated native HTTP options used by nodes and observers.</returns>
    [ConfigurationBinding]
    public static IOptions<HttpAdmissionLimits> CreateHttpOptions(IOptions<IsolatedKeyLoadAdmissionOptions> options)
    {
        var result = new OptionsManager<HttpAdmissionLimits>(new AdmissionFactory(options));
        _ = result.Value;
        return result;
    }

    [ConfigurationBinding]
    private sealed class AdmissionFactory : IOptionsFactory<HttpAdmissionLimits>
    {
        private readonly IsolatedKeyLoadAdmissionOptions policy;

        internal AdmissionFactory(IOptions<IsolatedKeyLoadAdmissionOptions> options)
        {
            ArgumentNullException.ThrowIfNull(options);
            policy = options.Value;
            policy.Validate();
        }

        public HttpAdmissionLimits Create(string name)
        {
            var result = new HttpAdmissionLimits
            {
                MaxRequests = policy.RequestsPerScope,
                MaxTenantRequests = policy.RequestsPerScope,
                MaxPrincipalRequests = policy.RequestsPerScope,
                ReservedControlRequests = policy.RequestsPerScope,
                MaxTenantControlRequests = policy.RequestsPerScope,
                MaxPrincipalControlRequests = policy.RequestsPerScope,
                MaxReservedBytes = policy.ReservedBytes,
            };
            result.Validate();
            return result;
        }
    }
}
