using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Annotype.Tooling;

/// <summary>
/// Emits <c>Annotype.Generated.&lt;AssemblyName&gt;.MetadataRegistry</c>, a reflection-free
/// <c>Annotype.IMetadataRegistry</c> that recreates the required metadata of every concrete contract
/// implementation of the current compilation.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MetadataRegistryGenerator : IIncrementalGenerator
{
    public const string HintName = "Annotype.MetadataRegistry.g.cs";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var implementations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax || node.IsKind(SyntaxKind.RecordDeclaration),
                static (syntax, cancellationToken) => Transform(syntax, cancellationToken))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .Collect();

        var assemblyName = context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? string.Empty);

        context.RegisterSourceOutput(implementations.Combine(assemblyName), static (output, source) => Execute(output, source.Left, source.Right));
    }

    private static ImplementationModel? Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not INamedTypeSymbol type
            || !ContractModel.IsInScope(type))
        {
            return null;
        }

        // Partial types: only the first declaration produces a model.
        var first = type.DeclaringSyntaxReferences[0];

        if (first.SyntaxTree != context.Node.SyntaxTree || first.Span != context.Node.Span)
        {
            return null;
        }

        var compilation = context.SemanticModel.Compilation;
        var symbols = KnownSymbols.Create(compilation);

        if (symbols is null)
        {
            return null;
        }

        var (required, _) = ContractModel.GetRequirements(type, symbols);
        var present = new List<(INamedTypeSymbol AttributeType, List<AttributeData> Applications)>();

        foreach (var requirement in required)
        {
            var applications = ContractModel.GetDirectApplications(type, requirement.AttributeType);

            // Missing metadata is reported by the analyzer (ANNO001).
            if (applications.Count > 0)
            {
                present.Add((requirement.AttributeType, applications));
            }
        }

        if (present.Count == 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var typeName = type.ToDisplayString();
        var typeLocation = type.Locations[0];
        var entries = ImmutableArray.CreateBuilder<MetadataEntry>();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var emitter = new AttributeEmitter(compilation);

        if (ContractModel.IsGenericOrNestedInGeneric(type))
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.GenericNotGenerated, typeLocation, typeName));
        }
        else if (!emitter.IsAccessible(type))
        {
            foreach (var (attributeType, _) in present)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.CannotReconstruct,
                    typeLocation,
                    typeName,
                    attributeType.ToDisplayString(),
                    "the type is not accessible from generated code (private, protected or file-local types are not supported)"));
            }
        }
        else
        {
            foreach (var (attributeType, applications) in present)
            {
                var attributeName = attributeType.ToDisplayString();

                if (ContractModel.GetAttributeUsage(attributeType, symbols).AllowMultiple)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.UnsupportedMultiple,
                        ContractModel.GetLocation(applications[0], type),
                        typeName,
                        attributeName,
                        applications.Count > 1
                            ? $"the attribute is applied {applications.Count} times; only one instance per type is supported"
                            : "the attribute is declared with AllowMultiple = true; required metadata attributes must be single-instance"));
                    continue;
                }

                if (applications.Count > 1)
                {
                    // Duplicate single-instance attributes are a compiler error already (CS0579).
                    continue;
                }

                var expression = emitter.Emit(applications[0], out var error);

                if (expression is not null)
                {
                    entries.Add(new MetadataEntry(AttributeEmitter.Format(attributeType), expression));
                }
                else if (error is not null)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.CannotReconstruct,
                        ContractModel.GetLocation(applications[0], type),
                        typeName,
                        attributeName,
                        error));
                }
            }
        }

        return new ImplementationModel(
            SortKey: type.ToDisplayString(AttributeEmitter.TypeFormat),
            ImplementationType: AttributeEmitter.Format(type),
            Entries: new EquatableArray<MetadataEntry>(entries.ToImmutable()),
            Diagnostics: new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()));
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<ImplementationModel> models, string assemblyName)
    {
        foreach (var model in models)
        {
            foreach (var diagnostic in model.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
        }

        var implementations = models
            .Where(static model => model.Entries.Length > 0)
            .OrderBy(static model => model.SortKey, StringComparer.Ordinal)
            .ToList();

        // Policy: no registry is generated for an assembly without any retrievable metadata.
        if (implementations.Count == 0)
        {
            return;
        }

        context.AddSource(HintName, SourceText.From(Render(implementations, assemblyName), Encoding.UTF8));
    }

    internal static string Render(IReadOnlyList<ImplementationModel> implementations, string assemblyName)
    {
        var builder = new StringBuilder();
        var version = typeof(MetadataRegistryGenerator).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        builder.Append("// <auto-generated/>\n");
        builder.Append("// Generated by the Annotype source generator. Do not edit.\n");
        builder.Append('\n');
        builder.Append("namespace ").Append(RegistryNaming.GetNamespace(assemblyName)).Append('\n');
        builder.Append("{\n");
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Reflection-free metadata registry of this assembly. Register it with\n");
        builder.Append("    /// <c>global::Annotype.Metadata.Register(MetadataRegistry.Instance)</c>.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    [global::System.CodeDom.Compiler.GeneratedCode(\"Annotype\", \"").Append(version).Append("\")]\n");
        builder.Append("    public sealed class MetadataRegistry : global::Annotype.IMetadataRegistry\n");
        builder.Append("    {\n");
        builder.Append("        private MetadataRegistry()\n");
        builder.Append("        {\n");
        builder.Append("        }\n");
        builder.Append('\n');
        builder.Append("        /// <summary>Gets the registry instance.</summary>\n");
        builder.Append("        public static MetadataRegistry Instance { get; } = new MetadataRegistry();\n");
        builder.Append('\n');
        builder.Append("        /// <inheritdoc />\n");
        builder.Append("        public bool TryGet(global::System.Type implementationType, global::System.Type attributeType, out global::System.Attribute metadata)\n");
        builder.Append("        {\n");

        foreach (var implementation in implementations)
        {
            builder.Append("            if (implementationType == typeof(").Append(implementation.ImplementationType).Append("))\n");
            builder.Append("            {\n");

            foreach (var entry in implementation.Entries.Items.OrderBy(static e => e.AttributeType, StringComparer.Ordinal))
            {
                builder.Append("                if (attributeType == typeof(").Append(entry.AttributeType).Append("))\n");
                builder.Append("                {\n");
                builder.Append("                    metadata = ").Append(entry.Expression).Append(";\n");
                builder.Append("                    return true;\n");
                builder.Append("                }\n");
                builder.Append('\n');
            }

            builder.Append("                metadata = null;\n");
            builder.Append("                return false;\n");
            builder.Append("            }\n");
            builder.Append('\n');
        }

        builder.Append("            metadata = null;\n");
        builder.Append("            return false;\n");
        builder.Append("        }\n");
        builder.Append("    }\n");
        builder.Append("}\n");

        return builder.ToString();
    }
}
