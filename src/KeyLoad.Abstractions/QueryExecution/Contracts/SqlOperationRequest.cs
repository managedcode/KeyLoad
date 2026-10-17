using System.Text.Json;

namespace KeyLoad;

/// <summary>Compiles one SQL SELECT or CALL into one existing authorized database operation.</summary>
/// <param name="Partition">Partition for Q1 SELECT; CALL binds scope through its canonical arguments.</param>
/// <param name="Sql">One admitted Q1/Q2 SELECT or Q1 CALL exact_public_operation(@arguments) statement.</param>
/// <param name="Parameters">Scalar SELECT parameters or one CALL argument-envelope object parameter.</param>
/// <param name="AllowFullScan">Explicit Q1 full-scan consent.</param>
/// <param name="Cursor">Optional Q1 continuation; unavailable for CALL.</param>
/// <param name="Version">Unified SQL envelope version.</param>
/// <param name="QueryDialectVersion">The explicit query-language selector; 1 preserves Q1.</param>
/// <param name="CommandId">Optional original caller-owned mutation identity; omitted for reads.</param>
/// <param name="OwnershipEpoch">Optional positive expected owner epoch for ordinary VALUES INSERT; omitted uses initial epoch one.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SqlOperationRequest)]
public sealed record SqlOperationRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Sql,
    [property: Orleans.Id(2)] Dictionary<string, JsonElement>? Parameters = null, [property: Orleans.Id(3)] bool AllowFullScan = false,
    [property: Orleans.Id(4)] string? Cursor = null, [property: Orleans.Id(5)] int Version = SqlOperationProtocol.Version,
    [property: Orleans.Id(6)] int QueryDialectVersion = SqlOperationProtocol.DefaultQueryDialectVersion,
    [property: Orleans.Id(SqlMutationIdentityFields.CommandId)] Guid? CommandId = null,
    [property: Orleans.Id(SqlMutationIdentityFields.OwnershipEpoch)] long? OwnershipEpoch = null);
