namespace KeyLoad;

/// <summary>Identifies explicit composition code which binds and validates typed application options.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class ConfigurationBindingAttribute : Attribute;
