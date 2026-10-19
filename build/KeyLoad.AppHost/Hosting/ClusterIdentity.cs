using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Hosting;

/// <summary>Shared database credentials for the Aspire-owned server processes.</summary>
[ConfigurationOptions]
internal sealed record ClusterIdentity(Guid Incarnation,
    [property: ConfigurationKeyName(ClusterIdentity.SigningParameter)] string SigningKey,
    [property: ConfigurationKeyName(ClusterIdentity.AdminParameter)] string AdminKey)
{
    internal const string SigningParameter = "signing-key";
    internal const string AdminParameter = "admin-key";
    internal const string IncarnationParameter = "incarnation";
    internal const string SectionName = "Parameters";
    internal const string InvalidIdentity = "Configure Parameters:incarnation, Parameters:signing-key and Parameters:admin-key for persistent database storage.";
    internal const int SecretBytes = 32;
    private const int MinimumAdminCharacters = 32;
    private const int MaximumAdminCharacters = 256;
    internal const string AdminPrefix = "root.";

    public ClusterIdentity() : this(Guid.Empty, string.Empty, string.Empty)
    {
    }

    internal bool IsValid()
    {
        Span<byte> decoded = stackalloc byte[SecretBytes];
        return Incarnation != Guid.Empty && Convert.TryFromBase64String(SigningKey, decoded, out var count)
            && count == SecretBytes && AdminKey is { Length: >= MinimumAdminCharacters and <= MaximumAdminCharacters }
            && AdminKey.StartsWith(AdminPrefix, StringComparison.Ordinal);
    }

    internal void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(InvalidIdentity);
        }
    }
}
