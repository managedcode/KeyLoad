using System.Text.Json;

namespace KeyLoad;

/// <summary>Compiles one SQL SELECT or CALL into one existing authorized database operation.</summary>
/// <param name="Partition">Partition for SELECT; CALL binds scope through its canonical arguments.</param>
/// <param name="Sql">One admitted SELECT, INSERT, CREATE TABLE, DROP TABLE or EXEC exact_public_operation @arguments statement.</param>
/// <param name="Parameters">Scalar SELECT parameters or one CALL argument-envelope object parameter.</param>
/// <param name="AllowFullScan">Explicit SQL full-scan consent.</param>
/// <param name="Cursor">Optional query continuation; unavailable for CALL.</param>
/// <param name="CommandId">Optional original caller-owned mutation identity; omitted for reads.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.SqlOperationRequest)]
public sealed record SqlOperationRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Sql,
    [property: Orleans.Id(2)] Dictionary<string, JsonElement>? Parameters = null, [property: Orleans.Id(3)] bool AllowFullScan = false,
    [property: Orleans.Id(4)] string? Cursor = null,
    [property: Orleans.Id(SqlMutationIdentityFields.CommandId)] Guid? CommandId = null);
