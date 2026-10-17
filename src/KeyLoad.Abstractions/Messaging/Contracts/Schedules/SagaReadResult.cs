namespace KeyLoad;

/// <summary>Contains the authorized saga state, or successful absence.</summary>
/// <param name="Saga">The projected saga; null means no matching saga exists.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PublicReadResultAliases.Saga)]
public sealed record SagaReadResult([property: Orleans.Id(0)] SagaInspection? Saga);
