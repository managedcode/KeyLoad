namespace KeyLoad.Core.Messaging;

/// <summary>Distinguishes the persisted schedule and saga discovery prefixes.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(DueWorkAliases.Kind)]
public enum DueWorkKind
{
    /// <summary>Scheduled recurring queue work.</summary>
    Schedule = 0,
    /// <summary>Persisted saga timeout work.</summary>
    Saga = 1
}
