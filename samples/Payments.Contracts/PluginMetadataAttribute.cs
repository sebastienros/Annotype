namespace Payments.Contracts;

/// <summary>
/// Describes a plugin type. The values are facts about the type, not about plugin instances.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PluginMetadataAttribute : Attribute
{
    public PluginMetadataAttribute(string name) => Name = name;

    public string Name { get; }

    public int Priority { get; set; }
}
