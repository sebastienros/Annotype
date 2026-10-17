using Microsoft.CodeAnalysis;

namespace Annotype.Tooling;

internal static class DiagnosticDescriptors
{
    private const string Category = "Annotype";
    private const string HelpLinkBase = "https://github.com/sebastienros/annotype/blob/main/docs/diagnostics.md#";

    public static readonly DiagnosticDescriptor MissingRequiredMetadata = new(
        id: "ANNO001",
        title: "Implementation is missing required metadata",
        messageFormat: "Type '{0}' implements '{1}' but is missing required attribute '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every concrete class implementing a contract interface must declare each attribute required by [RequiresMetadata] directly on its own declaration. Attributes inherited from base classes do not satisfy the requirement.",
        helpLinkUri: HelpLinkBase + "anno001");

    public static readonly DiagnosticDescriptor InvalidContract = new(
        id: "ANNO002",
        title: "Invalid metadata contract",
        messageFormat: "Invalid metadata contract on '{0}': {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[RequiresMetadata] must reference a non-abstract, closed attribute type that can be applied to classes.",
        helpLinkUri: HelpLinkBase + "anno002");

    public static readonly DiagnosticDescriptor CannotReconstruct = new(
        id: "ANNO003",
        title: "Metadata cannot be reconstructed by generated code",
        messageFormat: "Metadata '{1}' on type '{0}' cannot be reconstructed by generated code: {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated registry recreates attributes with their constructor and named arguments. The implementation type, the attribute constructor, its named members and every argument value must be accessible from generated code in the same assembly.",
        helpLinkUri: HelpLinkBase + "anno003");

    public static readonly DiagnosticDescriptor GenericNotGenerated = new(
        id: "ANNO004",
        title: "Generic implementation is excluded from the generated registry",
        messageFormat: "Type '{0}' is excluded from the generated metadata registry because generic types and types nested in generic types are not supported; declare a non-generic class deriving from it, annotated with the required metadata, to make its metadata retrievable",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "anno004");

    public static readonly DiagnosticDescriptor UnsupportedMultiple = new(
        id: "ANNO005",
        title: "Multiple metadata values are not supported",
        messageFormat: "Metadata '{1}' on type '{0}' is not supported by the generated registry: {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated registry supports a single instance of each required attribute per type. Required attributes must be declared with AllowMultiple = false.",
        helpLinkUri: HelpLinkBase + "anno005");
}
