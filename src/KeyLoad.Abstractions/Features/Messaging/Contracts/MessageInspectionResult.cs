namespace KeyLoad;

/// <summary>Contains the authorized message inspection, or explicit successful absence.</summary>
/// <param name="Message">The complete projected inspection; null means no message exists.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PublicReadResultAliases.Message)]
public sealed record MessageInspectionResult([property: Orleans.Id(0)] MessageInspection? Message);
