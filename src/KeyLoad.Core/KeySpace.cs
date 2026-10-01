using KeyLoad.Storage;

namespace KeyLoad.Core;

public static class KeySpace
{
    public static byte[] Partition(string space, PartitionRef partition, params object?[] suffix) => KeyCodec.Encode(
        new object?[] { space, partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey }.Concat(suffix).ToArray());
    public static byte[] Resource(string tenant, string database, string resource) => KeyCodec.Encode("catalog", tenant, database, resource);
    public static byte[] Principal(string principal) => KeyCodec.Encode("principal", principal);
    public static byte[] ApiKey(string id) => KeyCodec.Encode("api-key", id);
    public static byte[] Outcome(string principal, Guid id) => KeyCodec.Encode("outcome", principal, id);
    public static byte[] Applied { get; } = KeyCodec.Encode("system", "last-applied");
    public static byte[] Clock { get; } = KeyCodec.Encode("system", "clock");
}
public interface IAuthorizationPolicy
{
    void Require(PrincipalRecord principal, PartitionRef partition, string resource, Capability capability);
    bool CanReadRow(PrincipalRecord principal, RowAccess access);
    void RequireWriteRow(PrincipalRecord principal, RowAccess access);
    void RequireFieldUse(PrincipalRecord principal, ResourceDefinition resource, string path);
    void RequireFieldWrite(PrincipalRecord principal, ResourceDefinition resource, string path);
    void RequireReplacement(PrincipalRecord principal, ResourceDefinition resource, bool explicitReplacement);
    string Project(PrincipalRecord principal, SensitiveFieldPolicy[] policies, string json, out string[] omitted);
    void RequireWorkerInput(PrincipalRecord principal, ResourceDefinition resource);
}
public interface ICommitCoordinator
{
    Task<OperationResult> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson, CancellationToken cancellationToken = default);
    Task ReadBarrierAsync(CancellationToken cancellationToken = default);
}
public sealed class EmbeddedCoordinator(DatabaseEngine database) : ICommitCoordinator
{
    public Task<OperationResult> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(database.Apply(new(id, kind, principalId, DateTimeOffset.UtcNow, payloadJson)));
    }
    public Task ReadBarrierAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
}
