namespace KeyLoad.UnitTests.Features.Search;

internal static class TextMaintenanceLifetimeNativeProtocol
{
    internal const string Consumer = "d4-text-native";
    internal const string Field = "/text";
    internal const string RootDirectory = "d4-text-native-files";
    internal const string Missing = "The native text lifetime fixture value is unavailable.";
    internal const int Generation = 1;
    internal const int RowCount = 2;
    internal const int LifetimeCount = 1;
    internal const string UkrainianQuery = "ПРИВІТ";
    internal const double SingleBranchScore = 1d / 61d;
    internal const int Revision = 1;
    internal const int NextIndex = 1;
    internal const int NoEffects = 0;
    internal const string MutationKind = "putDocument";
    internal const string AfterCancellationId = "after-native-text-cancellation";
    internal const string AfterCancellationJson = "{\"value\":\"native-store-still-healthy\"}";
    internal static readonly string[] Mutations = [MutationDiscriminatorNames.PutDocument,
        MutationDiscriminatorNames.PatchDocument, MutationDiscriminatorNames.DeleteDocument];
}
