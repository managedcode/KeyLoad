using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Creates native options around strict immutable host inputs without changing their parsing contracts.</summary>
[KeyLoad.ConfigurationBinding]
internal static class ComparisonHostOptionsRegistration
{
    internal static IOptions<T> Bind<T>(Func<T> readValidated) where T : class
    {
        IOptions<T> options = new OptionsManager<T>(new StrictHostOptionsFactory<T>(readValidated));
        _ = options.Value;
        return options;
    }

    private sealed class StrictHostOptionsFactory<T>(Func<T> readValidated) : OptionsFactory<T>([], [], []) where T : class
    {
        protected override T CreateInstance(string name) => readValidated();
    }
}
