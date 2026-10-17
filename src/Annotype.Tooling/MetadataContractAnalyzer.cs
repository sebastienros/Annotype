using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Annotype.Tooling;

/// <summary>
/// Enforces [RequiresMetadata] contracts declared on interfaces and classes: reports ANNO001 on concrete classes
/// missing a required attribute and ANNO002 on invalid contracts.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MetadataContractAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        DiagnosticDescriptors.MissingRequiredMetadata,
        DiagnosticDescriptors.InvalidContract);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var symbols = KnownSymbols.Create(start.Compilation);

            if (symbols is null)
            {
                return;
            }

            start.RegisterSymbolAction(context => AnalyzeType(context, symbols), SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, KnownSymbols symbols)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind is TypeKind.Interface or TypeKind.Class)
        {
            AnalyzeContract(context, type, symbols);
        }

        if (!ContractModel.IsInScope(type))
        {
            return;
        }

        var (required, invalid) = ContractModel.GetRequirements(type, symbols);
        var location = type.Locations.Length > 0 ? type.Locations[0] : Location.None;

        // Invalid contracts declared in source are reported on the contract itself. Contracts coming from
        // referenced assemblies have no source to point at, so the implementation reports them instead.
        var reported = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var requirement in invalid)
        {
            if (requirement.Application.ApplicationSyntaxReference is null && reported.Add(requirement.Contract))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.InvalidContract,
                    location,
                    requirement.Contract.ToDisplayString(),
                    requirement.Error));
            }
        }

        foreach (var requirement in required)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (ContractModel.GetDirectApplications(type, requirement.AttributeType).Count > 0)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingRequiredMetadata,
                location,
                type.ToDisplayString(),
                ContractModel.DescribeContracts(type, requirement.Contracts),
                requirement.AttributeType.ToDisplayString()));
        }
    }

    private static void AnalyzeContract(SymbolAnalysisContext context, INamedTypeSymbol contract, KnownSymbols symbols)
    {
        foreach (var requirement in ContractModel.GetDeclaredRequirements(contract, symbols))
        {
            if (requirement.Error is null)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidContract,
                ContractModel.GetLocation(requirement.Application, contract),
                contract.ToDisplayString(),
                requirement.Error));
        }
    }
}
