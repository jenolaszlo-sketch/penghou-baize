namespace Penghou.Baize.Tools.Schema;

/// <summary>
/// Supplies a human-readable description for a schema property or parameter,
/// emitted as the <c>description</c> keyword by <see cref="JsonSchemaGenerator"/>.
/// </summary>
/// <param name="description">The schema description text.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class SchemaDescriptionAttribute(string description) : Attribute
{
    /// <summary>
    /// Gets the schema description text.
    /// </summary>
    public string Description { get; } = description;
}
