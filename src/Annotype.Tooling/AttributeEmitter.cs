using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Annotype.Tooling;

/// <summary>
/// Turns an <see cref="AttributeData"/> into a C# expression that recreates the same attribute instance:
/// <c>new Attribute(constructor arguments) { NamedMember = value, ... }</c>. This is the same sequence the runtime
/// uses when it materializes custom attributes, so no state of the attribute type needs to be known.
/// </summary>
/// <remarks>
/// The emitted code is valid C# 7.3 and only uses <c>global::</c> qualified names.
/// </remarks>
internal sealed class AttributeEmitter
{
    public static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            & ~SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private readonly Compilation _compilation;

    public AttributeEmitter(Compilation compilation) => _compilation = compilation;

    /// <summary>
    /// Returns <see langword="null"/> and sets <paramref name="error"/> when the attribute cannot be recreated.
    /// Returns <see langword="null"/> with no error when the attribute does not bind (the compiler reports it).
    /// </summary>
    public string? Emit(AttributeData attribute, out string? error)
    {
        error = null;
        var attributeClass = attribute.AttributeClass;
        var constructor = attribute.AttributeConstructor;

        if (attributeClass is null || constructor is null || attributeClass.TypeKind == TypeKind.Error)
        {
            return null;
        }

        if (!IsAccessible(attributeClass))
        {
            error = $"attribute type '{attributeClass.ToDisplayString()}' is not accessible from generated code";
            return null;
        }

        if (!IsAccessible(constructor))
        {
            error = $"constructor '{constructor.ToDisplayString()}' is not accessible from generated code";
            return null;
        }

        if (attribute.ConstructorArguments.Length != constructor.Parameters.Length)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append("new ").Append(Format(attributeClass)).Append('(');

        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            var parameter = constructor.Parameters[i];
            var value = EmitValue(attribute.ConstructorArguments[i], parameter.Type, out error);

            if (value is null)
            {
                error = error is null ? null : $"argument '{parameter.Name}': {error}";
                return null;
            }

            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(value);
        }

        builder.Append(')');

        if (attribute.NamedArguments.Length > 0)
        {
            builder.Append(" { ");

            for (var i = 0; i < attribute.NamedArguments.Length; i++)
            {
                var named = attribute.NamedArguments[i];
                var memberType = GetWritableMemberType(attributeClass, named.Key, out error);

                if (memberType is null)
                {
                    return null;
                }

                var value = EmitValue(named.Value, memberType, out error);

                if (value is null)
                {
                    error = error is null ? null : $"member '{named.Key}': {error}";
                    return null;
                }

                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(EscapeIdentifier(named.Key)).Append(" = ").Append(value);
            }

            builder.Append(" }");
        }

        return builder.ToString();
    }

    public bool IsAccessible(ISymbol symbol)
    {
        for (var type = symbol as INamedTypeSymbol ?? symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            if (type.IsFileLocal)
            {
                return false;
            }
        }

        return _compilation.IsSymbolAccessibleWithin(symbol, _compilation.Assembly);
    }

    private ITypeSymbol? GetWritableMemberType(INamedTypeSymbol attributeClass, string name, out string? error)
    {
        error = null;

        for (var type = attributeClass; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers(name))
            {
                switch (member)
                {
                    case IPropertySymbol { IsStatic: false, IsIndexer: false } property:
                        if (property.SetMethod is null || !IsAccessible(property.SetMethod))
                        {
                            error = $"property '{name}' has no setter accessible from generated code";
                            return null;
                        }

                        return property.Type;

                    case IFieldSymbol { IsStatic: false } field:
                        if (field.IsReadOnly || field.IsConst || !IsAccessible(field))
                        {
                            error = $"field '{name}' is not writable from generated code";
                            return null;
                        }

                        return field.Type;
                }
            }
        }

        // Does not bind; the compiler reports it.
        return null;
    }

    private string? EmitValue(TypedConstant constant, ITypeSymbol target, out string? error)
    {
        error = null;

        if (constant.Kind == TypedConstantKind.Error)
        {
            return null;
        }

        if (constant.IsNull)
        {
            return $"({Format(target)})null";
        }

        switch (constant.Kind)
        {
            case TypedConstantKind.Primitive:
                var literal = FormatPrimitive(constant.Value);

                if (literal is null)
                {
                    error = $"value '{constant.Value}' of type '{constant.Value?.GetType()}' is not supported";
                    return null;
                }

                return Convert(literal, constant.Type, target);

            case TypedConstantKind.Enum:
                if (constant.Type is null || !IsAccessible(constant.Type))
                {
                    error = $"enum type '{constant.Type?.ToDisplayString()}' is not accessible from generated code";
                    return null;
                }

                var underlying = FormatPrimitive(constant.Value);

                if (underlying is null)
                {
                    error = $"enum value '{constant.Value}' is not supported";
                    return null;
                }

                return Convert($"({Format(constant.Type)})({underlying})", constant.Type, target);

            case TypedConstantKind.Type:
                if (constant.Value is not ITypeSymbol type || type.TypeKind == TypeKind.Error)
                {
                    return null;
                }

                if (!IsAccessibleType(type))
                {
                    error = $"type '{type.ToDisplayString()}' is not accessible from generated code";
                    return null;
                }

                return Convert($"typeof({FormatTypeOf(type)})", constant.Type, target);

            case TypedConstantKind.Array:
                if (constant.Type is not IArrayTypeSymbol arrayType || !arrayType.IsSZArray)
                {
                    error = "only single-dimensional arrays are supported";
                    return null;
                }

                if (!IsAccessibleType(arrayType.ElementType))
                {
                    error = $"type '{arrayType.ElementType.ToDisplayString()}' is not accessible from generated code";
                    return null;
                }

                var elementType = Format(arrayType.ElementType);

                if (constant.Values.IsEmpty)
                {
                    return Convert($"new {elementType}[0]", arrayType, target);
                }

                var elements = new List<string>(constant.Values.Length);

                foreach (var element in constant.Values)
                {
                    var value = EmitValue(element, arrayType.ElementType, out error);

                    if (value is null)
                    {
                        return null;
                    }

                    elements.Add(value);
                }

                return Convert($"new {elementType}[] {{ {string.Join(", ", elements)} }}", arrayType, target);

            default:
                error = $"constant kind '{constant.Kind}' is not supported";
                return null;
        }
    }

    private bool IsAccessibleType(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsAccessibleType(array.ElementType);
            case INamedTypeSymbol named:
                if (!IsAccessible(named))
                {
                    return false;
                }

                foreach (var argument in named.TypeArguments)
                {
                    if (argument.TypeKind != TypeKind.Error && !IsAccessibleType(argument))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return _compilation.IsSymbolAccessibleWithin(type, _compilation.Assembly);
        }
    }

    private static string Convert(string expression, ITypeSymbol? source, ITypeSymbol target)
        => source is not null && SymbolEqualityComparer.Default.Equals(source, target)
            ? expression
            : $"({Format(target)})({expression})";

    public static string Format(ITypeSymbol type) => type.ToDisplayString(TypeFormat);

    /// <summary>
    /// Formats a type for a typeof() expression, including unbound generic types such as <c>List&lt;&gt;</c>.
    /// </summary>
    public static string FormatTypeOf(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { IsUnboundGenericType: true } named)
        {
            return Format(type);
        }

        string prefix;

        if (named.ContainingType is { } containing)
        {
            var container = containing.IsGenericType
                ? FormatTypeOf(containing.OriginalDefinition.ConstructUnboundGenericType())
                : Format(containing);
            prefix = container + ".";
        }
        else
        {
            prefix = named.ContainingNamespace is null || named.ContainingNamespace.IsGlobalNamespace
                ? "global::"
                : named.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".";
        }

        var name = EscapeIdentifier(named.Name);

        return named.Arity > 0
            ? prefix + name + "<" + new string(',', named.Arity - 1) + ">"
            : prefix + name;
    }

    public static string EscapeIdentifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    /// <summary>
    /// Formats a constant as a C# literal that has exactly the type of the value.
    /// </summary>
    public static string? FormatPrimitive(object? value)
    {
        var invariant = CultureInfo.InvariantCulture;

        switch (value)
        {
            case bool b:
                return b ? "true" : "false";
            case string s:
                return SymbolDisplay.FormatLiteral(s, quote: true);
            case char c:
                return SymbolDisplay.FormatLiteral(c, quote: true);
            case sbyte sb:
                return "(sbyte)(" + sb.ToString(invariant) + ")";
            case byte by:
                return "(byte)(" + by.ToString(invariant) + ")";
            case short sh:
                return "(short)(" + sh.ToString(invariant) + ")";
            case ushort us:
                return "(ushort)(" + us.ToString(invariant) + ")";
            case int i:
                return i == int.MinValue ? "int.MinValue" : i.ToString(invariant);
            case uint ui:
                return ui.ToString(invariant) + "U";
            case long l:
                return l == long.MinValue ? "long.MinValue" : l.ToString(invariant) + "L";
            case ulong ul:
                return ul.ToString(invariant) + "UL";
            case float f:
                if (float.IsNaN(f))
                {
                    return "float.NaN";
                }

                if (float.IsPositiveInfinity(f))
                {
                    return "float.PositiveInfinity";
                }

                if (float.IsNegativeInfinity(f))
                {
                    return "float.NegativeInfinity";
                }

                return IsNegativeZero(f) ? "-0F" : f.ToString("G9", invariant) + "F";
            case double d:
                if (double.IsNaN(d))
                {
                    return "double.NaN";
                }

                if (double.IsPositiveInfinity(d))
                {
                    return "double.PositiveInfinity";
                }

                if (double.IsNegativeInfinity(d))
                {
                    return "double.NegativeInfinity";
                }

                return IsNegativeZero(d) ? "-0D" : d.ToString("G17", invariant) + "D";
            default:
                return null;
        }
    }

    private static bool IsNegativeZero(double value)
        => value == 0 && BitConverter.DoubleToInt64Bits(value) != 0;
}
