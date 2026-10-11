namespace KeyLoad;

/// <summary>Contains the authorized document, or explicit successful absence.</summary>
/// <param name="Document">The complete projected document; null means no document exists.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PublicReadResultAliases.Document)]
public sealed record DocumentReadResult([property: Orleans.Id(0)] DocumentResult? Document);
