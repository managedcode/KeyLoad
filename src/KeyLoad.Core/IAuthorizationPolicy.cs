namespace KeyLoad.Core;

/// <summary>Enforces database-persisted operation, row and field authority.</summary>
public interface IAuthorizationPolicy
{
    /// <summary>Requires the operation capability in the specified resource scope.</summary>
    void Require(PrincipalRecord principal, PartitionRef partition, string resource, Capability capability);
    /// <summary>Determines whether the persisted principal may see the row scope.</summary>
    bool CanReadRow(PrincipalRecord principal, RowAccess access);
    /// <summary>Requires permission to write the row scope.</summary>
    void RequireWriteRow(PrincipalRecord principal, RowAccess access);
    /// <summary>Requires raw field-use permission before filtering, ordering or searching.</summary>
    void RequireFieldUse(PrincipalRecord principal, ResourceDefinition resource, string path);
    /// <summary>Requires permission to modify the field path.</summary>
    void RequireFieldWrite(PrincipalRecord principal, ResourceDefinition resource, string path);
    /// <summary>Requires an explicit authorized replacement when protected data is redacted.</summary>
    void RequireReplacement(PrincipalRecord principal, ResourceDefinition resource, bool explicitReplacement);
    /// <summary>Projects JSON according to persisted field policy and reports omitted paths.</summary>
    string Project(PrincipalRecord principal, IReadOnlyList<SensitiveFieldPolicy> policies, string json, out string[] omitted);
    /// <summary>Requires access to every protected field needed for worker processing.</summary>
    void RequireWorkerInput(PrincipalRecord principal, ResourceDefinition resource);
    /// <summary>Requires raw read and use of every configured replay payload and header field.</summary>
    void RequireReplayInput(PrincipalRecord principal, ResourceDefinition resource);
}
