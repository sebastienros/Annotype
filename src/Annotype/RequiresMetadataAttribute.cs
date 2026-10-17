using System;

namespace Annotype;

/// <summary>
/// Declares that every concrete class implementing the annotated interface, or deriving from the annotated class
/// (including the class itself when it is concrete), must carry an attribute of <see cref="AttributeType"/>
/// directly on its declaration.
/// </summary>
/// <remarks>
/// The requirement is enforced at compile time by the Annotype analyzer (diagnostic <c>ANNO001</c>) in every
/// compilation that references the Annotype package directly. The value of the attribute is captured by the
/// Annotype source generator and can be retrieved with <see cref="Metadata.Get{TImplementation, TAttribute}"/>
/// once the generated registry is registered.
/// </remarks>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresMetadataAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiresMetadataAttribute"/> class.
    /// </summary>
    /// <param name="attributeType">The attribute type that implementations must declare.</param>
    public RequiresMetadataAttribute(Type attributeType) => AttributeType = attributeType;

    /// <summary>
    /// Gets the attribute type that implementations must declare.
    /// </summary>
    public Type AttributeType { get; }
}
