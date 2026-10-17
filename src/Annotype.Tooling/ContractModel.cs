using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Annotype.Tooling;

/// <summary>
/// Well-known symbols resolved once per compilation.
/// </summary>
internal sealed class KnownSymbols
{
    public const string RequiresMetadataMetadataName = "Annotype.RequiresMetadataAttribute";

    private KnownSymbols(INamedTypeSymbol requiresMetadata, INamedTypeSymbol attribute, INamedTypeSymbol? attributeUsage)
    {
        RequiresMetadata = requiresMetadata;
        Attribute = attribute;
        AttributeUsage = attributeUsage;
    }

    public INamedTypeSymbol RequiresMetadata { get; }

    public INamedTypeSymbol Attribute { get; }

    public INamedTypeSymbol? AttributeUsage { get; }

    public static KnownSymbols? Create(Compilation compilation)
    {
        var requiresMetadata = compilation.GetTypeByMetadataName(RequiresMetadataMetadataName);
        var attribute = compilation.GetTypeByMetadataName("System.Attribute");

        if (requiresMetadata is null || attribute is null)
        {
            return null;
        }

        return new KnownSymbols(requiresMetadata, attribute, compilation.GetTypeByMetadataName("System.AttributeUsageAttribute"));
    }
}

/// <summary>
/// One [RequiresMetadata] application on a contract interface.
/// </summary>
internal sealed class Requirement
{
    public Requirement(INamedTypeSymbol contract, AttributeData application, INamedTypeSymbol? attributeType, string? error)
    {
        Contract = contract;
        Application = application;
        AttributeType = attributeType;
        Error = error;
    }

    public INamedTypeSymbol Contract { get; }

    public AttributeData Application { get; }

    /// <summary>The required attribute type, or <see langword="null"/> when the requirement is invalid or does not bind.</summary>
    public INamedTypeSymbol? AttributeType { get; }

    /// <summary>The reason the requirement is invalid, or <see langword="null"/>.</summary>
    public string? Error { get; }

    public bool IsValid => AttributeType is not null && Error is null;
}

/// <summary>
/// A required attribute type and the contracts of an implementation that require it.
/// </summary>
internal sealed class RequiredAttribute
{
    public RequiredAttribute(INamedTypeSymbol attributeType) => AttributeType = attributeType;

    public INamedTypeSymbol AttributeType { get; }

    public List<INamedTypeSymbol> Contracts { get; } = [];
}

internal static class ContractModel
{
    private const int AttributeTargetsClass = 4;

    /// <summary>
    /// Concrete classes (including record classes) are validated. Abstract and static classes, structs,
    /// interfaces, enums and delegates are not.
    /// </summary>
    public static bool IsInScope(INamedTypeSymbol type)
        => type.TypeKind == TypeKind.Class && !type.IsAbstract && !type.IsStatic && !type.IsImplicitlyDeclared;

    /// <summary>
    /// Returns the [RequiresMetadata] requirements declared directly on <paramref name="contract"/>.
    /// </summary>
    public static List<Requirement> GetDeclaredRequirements(INamedTypeSymbol contract, KnownSymbols symbols)
    {
        var result = new List<Requirement>();

        foreach (var attribute in contract.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.RequiresMetadata))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length != 1)
            {
                // Does not bind; the compiler reports it.
                continue;
            }

            var argument = attribute.ConstructorArguments[0];

            if (argument.Kind == TypedConstantKind.Error)
            {
                continue;
            }

            if (argument.IsNull || argument.Value is not ITypeSymbol requiredType)
            {
                result.Add(new Requirement(contract, attribute, null, "the required attribute type must not be null"));
                continue;
            }

            if (requiredType.TypeKind == TypeKind.Error)
            {
                continue;
            }

            result.Add(new Requirement(contract, attribute, requiredType as INamedTypeSymbol, Validate(requiredType, symbols)));
        }

        return result;
    }

    private static string? Validate(ITypeSymbol requiredType, KnownSymbols symbols)
    {
        if (requiredType is INamedTypeSymbol { IsUnboundGenericType: true })
        {
            return $"'{requiredType.ToDisplayString()}' is an open generic type";
        }

        if (requiredType is not INamedTypeSymbol named || !InheritsFrom(named, symbols.Attribute))
        {
            return $"'{requiredType.ToDisplayString()}' does not derive from System.Attribute";
        }

        if (named.IsAbstract)
        {
            return $"'{requiredType.ToDisplayString()}' is abstract and cannot be applied";
        }

        var (validOn, _) = GetAttributeUsage(named, symbols);

        if ((validOn & AttributeTargetsClass) == 0)
        {
            return $"'{requiredType.ToDisplayString()}' cannot be applied to classes";
        }

        return null;
    }

    /// <summary>
    /// Returns the types whose [RequiresMetadata] apply to <paramref name="type"/>: every interface it implements,
    /// directly or through base classes and inherited interfaces, then the type itself and its base classes.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> GetContractCandidates(INamedTypeSymbol type)
    {
        foreach (var contract in type.AllInterfaces)
        {
            yield return contract;
        }

        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            yield return current;
        }
    }

    /// <summary>
    /// Collects the requirements of every contract of <paramref name="type"/> (see <see cref="GetContractCandidates"/>).
    /// Valid requirements are grouped by attribute type, in a stable order.
    /// </summary>
    public static (List<RequiredAttribute> Required, List<Requirement> Invalid) GetRequirements(INamedTypeSymbol type, KnownSymbols symbols)
    {
        var required = new List<RequiredAttribute>();
        var invalid = new List<Requirement>();

        foreach (var contract in GetContractCandidates(type))
        {
            foreach (var requirement in GetDeclaredRequirements(contract, symbols))
            {
                if (!requirement.IsValid)
                {
                    if (requirement.Error is not null)
                    {
                        invalid.Add(requirement);
                    }

                    continue;
                }

                var entry = required.Find(r => SymbolEqualityComparer.Default.Equals(r.AttributeType, requirement.AttributeType));

                if (entry is null)
                {
                    entry = new RequiredAttribute(requirement.AttributeType!);
                    required.Add(entry);
                }

                if (!entry.Contracts.Exists(c => SymbolEqualityComparer.Default.Equals(c, contract)))
                {
                    entry.Contracts.Add(contract);
                }
            }
        }

        return (required, invalid);
    }

    /// <summary>
    /// Returns the attributes of type <paramref name="attributeType"/> declared directly on <paramref name="type"/>.
    /// Inherited attributes are deliberately ignored (direct-only policy).
    /// </summary>
    public static List<AttributeData> GetDirectApplications(INamedTypeSymbol type, INamedTypeSymbol attributeType)
    {
        var result = new List<AttributeData>();

        foreach (var attribute in type.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            {
                result.Add(attribute);
            }
        }

        return result;
    }

    /// <summary>
    /// Reads [AttributeUsage] from the attribute type or its closest base declaring it.
    /// </summary>
    public static (int ValidOn, bool AllowMultiple) GetAttributeUsage(INamedTypeSymbol attributeType, KnownSymbols symbols)
    {
        for (var current = attributeType; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (symbols.AttributeUsage is null || !SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.AttributeUsage))
                {
                    continue;
                }

                var validOn = attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int targets
                    ? targets
                    : -1;
                var allowMultiple = false;

                foreach (var named in attribute.NamedArguments)
                {
                    if (named.Key == "AllowMultiple" && named.Value.Value is bool value)
                    {
                        allowMultiple = value;
                    }
                }

                return (validOn, allowMultiple);
            }
        }

        // AttributeTargets.All, AllowMultiple = false.
        return (-1, false);
    }

    public static bool IsGenericOrNestedInGeneric(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Describes how <paramref name="type"/> is bound to <paramref name="contracts"/>, for example
    /// <c>implements 'IPlugin'</c>, <c>derives from 'PluginBase'</c> or <c>is marked with [RequiresMetadata]</c>.
    /// </summary>
    public static string DescribeContracts(INamedTypeSymbol type, List<INamedTypeSymbol> contracts)
    {
        var interfaces = new List<string>();
        var baseClasses = new List<string>();
        var self = false;

        foreach (var contract in contracts)
        {
            if (SymbolEqualityComparer.Default.Equals(contract, type))
            {
                self = true;
            }
            else if (contract.TypeKind == TypeKind.Interface)
            {
                interfaces.Add(contract.ToDisplayString());
            }
            else
            {
                baseClasses.Add(contract.ToDisplayString());
            }
        }

        var phrases = new List<string>();

        if (interfaces.Count > 0)
        {
            phrases.Add("implements '" + string.Join("', '", interfaces) + "'");
        }

        if (baseClasses.Count > 0)
        {
            phrases.Add("derives from '" + string.Join("', '", baseClasses) + "'");
        }

        if (self)
        {
            phrases.Add("is marked with [RequiresMetadata]");
        }

        return string.Join(" and ", phrases);
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    public static Location GetLocation(AttributeData attribute, ISymbol fallback)
    {
        var reference = attribute.ApplicationSyntaxReference;

        if (reference is not null)
        {
            return reference.SyntaxTree.GetLocation(reference.Span);
        }

        return fallback.Locations.Length > 0 ? fallback.Locations[0] : Location.None;
    }
}
