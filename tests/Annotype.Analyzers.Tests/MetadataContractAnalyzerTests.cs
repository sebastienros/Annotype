using System.Linq;
using System.Threading.Tasks;
using Annotype.Tests;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Annotype.Analyzers.Tests;

public class MetadataContractAnalyzerTests
{
    internal const string Contracts = """
        using System;
        using Annotype;

        namespace Payments.Contracts
        {
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
            public sealed class PluginMetadataAttribute : Attribute
            {
                public PluginMetadataAttribute(string name) => Name = name;
                public string Name { get; }
                public int Priority { get; set; }
            }

            [RequiresMetadata(typeof(PluginMetadataAttribute))]
            public interface IPlugin
            {
                void Execute();
            }
        }
        """;

    private static async Task<Diagnostic[]> AnalyzeAsync(string source)
    {
        var compilation = TestCompilation.Create([Contracts, source]);
        return [.. await TestCompilation.GetAnalyzerDiagnosticsAsync(compilation)];
    }

    private static string GetLocationText(Diagnostic diagnostic)
        => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    [Fact]
    public async Task MissingAnnotationOnDirectImplementationReportsOneError()
    {
        var diagnostics = await AnalyzeAsync("""
            using Payments.Contracts;

            public sealed class InvalidPlugin : IPlugin
            {
                public void Execute() { }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ANNO001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("InvalidPlugin", GetLocationText(diagnostic));
        Assert.Equal(
            "Type 'InvalidPlugin' implements 'Payments.Contracts.IPlugin' but is missing required attribute 'Payments.Contracts.PluginMetadataAttribute'",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ValidAnnotationReportsNothing()
    {
        var diagnostics = await AnalyzeAsync("""
            using Payments.Contracts;

            [PluginMetadata("payments", Priority = 10)]
            public sealed class PaymentPlugin : IPlugin
            {
                public void Execute() { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ContractInheritedThroughInterfaceIsEnforced()
    {
        var diagnostics = await AnalyzeAsync("""
            using Payments.Contracts;

            public interface IAdvancedPlugin : IPlugin { }

            public class AdvancedPlugin : IAdvancedPlugin
            {
                public void Execute() { }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ANNO001", diagnostic.Id);
        Assert.Equal("AdvancedPlugin", GetLocationText(diagnostic));
    }

    [Fact]
    public async Task ConcreteDerivedClassOfImplementationIsEnforced()
    {
        var diagnostics = await AnalyzeAsync("""
            using Payments.Contracts;

            [PluginMetadata("base")]
            public class BasePlugin : IPlugin
            {
                public void Execute() { }
            }

            public class DerivedPlugin : BasePlugin { }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ANNO001", diagnostic.Id);
        Assert.Equal("DerivedPlugin", GetLocationText(diagnostic));
    }

    [Fact]
    public async Task AbstractImplementationIsIgnoredButConcreteDerivedClassIsChecked()
    {
        var diagnostics = await AnalyzeAsync("""
            using Payments.Contracts;

            public abstract class PluginBase : IPlugin
            {
                public abstract void Execute();
            }

            public class ConcretePlugin : PluginBase
            {
                public override void Execute() { }
            }

            [PluginMetadata("ok")]
            public class AnnotatedPlugin : PluginBase
            {
                public override void Execute() { }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ConcretePlugin", GetLocationText(diagnostic));
    }

    [Fact]
    public async Task TwoContractsRequiringSameAttributeReportOneError()
    {
        var diagnostics = await AnalyzeAsync("""
            using Annotype;
            using Payments.Contracts;

            [RequiresMetadata(typeof(PluginMetadataAttribute))]
            public interface IOtherPlugin { }

            public class DualPlugin : IPlugin, IOtherPlugin
            {
                public void Execute() { }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ANNO001", diagnostic.Id);
        var message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("Payments.Contracts.IPlugin", message, System.StringComparison.Ordinal);
        Assert.Contains("IOtherPlugin", message, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultipleRequiredAttributesAreReportedIndividually()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;
            using Annotype;
            using Payments.Contracts;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class OwnerAttribute : Attribute { }

            [RequiresMetadata(typeof(PluginMetadataAttribute))]
            [RequiresMetadata(typeof(OwnerAttribute))]
            public interface IOwnedPlugin { }

            [PluginMetadata("x")]
            public class OwnedPlugin : IOwnedPlugin { }

            public class UnownedPlugin : IOwnedPlugin { }
            """);

        Assert.Collection(
            diagnostics,
            d => Assert.Equal("Type 'OwnedPlugin' implements 'IOwnedPlugin' but is missing required attribute 'OwnerAttribute'", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)),
            d => Assert.Equal("Type 'UnownedPlugin' implements 'IOwnedPlugin' but is missing required attribute 'OwnerAttribute'", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)),
            d => Assert.Equal("Type 'UnownedPlugin' implements 'IOwnedPlugin' but is missing required attribute 'Payments.Contracts.PluginMetadataAttribute'", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.All(diagnostics, static d => Assert.Equal("ANNO001", d.Id));
    }

    [Fact]
    public async Task PartialDeclarationReportsOneError()
    {
        var diagnostics = await AnalyzeAsync("""
            using Payments.Contracts;

            public partial class PartialPlugin : IPlugin { }

            public partial class PartialPlugin
            {
                public void Execute() { }
            }
            """);

        Assert.Equal("ANNO001", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task PartialDeclarationIsSatisfiedByAttributeOnAnyPart()
    {
        var diagnostics = await AnalyzeAsync("""
            using Payments.Contracts;

            public partial class PartialPlugin : IPlugin { }

            [PluginMetadata("partial")]
            public partial class PartialPlugin
            {
                public void Execute() { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task IdenticallyNamedAttributeFromAnotherNamespaceDoesNotSatisfyContract()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;

            namespace Impostor
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class PluginMetadataAttribute : Attribute
                {
                    public PluginMetadataAttribute(string name) { }
                }

                [PluginMetadata("fake")]
                public class FakePlugin : Payments.Contracts.IPlugin
                {
                    public void Execute() { }
                }
            }
            """);

        Assert.Equal("ANNO001", Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData("typeof(string)", "'string' does not derive from System.Attribute")]
    [InlineData("null", "must not be null")]
    [InlineData("typeof(AbstractAttribute)", "is abstract")]
    [InlineData("typeof(MethodOnlyAttribute)", "cannot be applied to classes")]
    [InlineData("typeof(GenericAttribute<>)", "open generic")]
    public async Task InvalidContractReportsAnno002OnTheAttribute(string argument, string reason)
    {
        var diagnostics = await AnalyzeAsync($$"""
            using System;
            using Annotype;

            public abstract class AbstractAttribute : Attribute { }

            [AttributeUsage(AttributeTargets.Method)]
            public sealed class MethodOnlyAttribute : Attribute { }

            public sealed class GenericAttribute<T> : Attribute { }

            [RequiresMetadata({{argument}})]
            public interface IBroken { }

            public class Broken : IBroken { }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ANNO002", diagnostic.Id);
        Assert.Equal($"RequiresMetadata({argument})", GetLocationText(diagnostic));
        Assert.Contains(reason, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContractFromReferencedAssemblyIsEnforced()
    {
        var contracts = TestCompilation.ToReference(TestCompilation.Create(Contracts, "Payments.Contracts"));
        var compilation = TestCompilation.Create("""
            using Payments.Contracts;

            [PluginMetadata("ok")]
            public sealed class PaymentPlugin : IPlugin { public void Execute() { } }

            public sealed class InvalidPlugin : IPlugin { public void Execute() { } }
            """, "Payments.Plugins", [contracts]);

        var diagnostics = await TestCompilation.GetAnalyzerDiagnosticsAsync(compilation);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ANNO001", diagnostic.Id);
        Assert.Equal("InvalidPlugin", GetLocationText(diagnostic));
    }

    [Fact]
    public async Task InvalidContractFromReferencedAssemblyIsReportedOnImplementation()
    {
        var contracts = TestCompilation.ToReference(TestCompilation.Create("""
            using Annotype;
            namespace Broken.Contracts
            {
                [RequiresMetadata(typeof(string))]
                public interface IBroken { }
            }
            """, "Broken.Contracts"));
        var compilation = TestCompilation.Create("""
            public sealed class Implementation : Broken.Contracts.IBroken { }
            """, "Broken.Plugins", [contracts]);

        var diagnostics = await TestCompilation.GetAnalyzerDiagnosticsAsync(compilation);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ANNO002", diagnostic.Id);
        Assert.Equal("Implementation", GetLocationText(diagnostic));
        Assert.Contains("Broken.Contracts.IBroken", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordClassIsValidated()
    {
        var diagnostics = await AnalyzeAsync("""
            using Payments.Contracts;

            public record RecordPlugin(int Value) : IPlugin
            {
                public void Execute() { }
            }

            [PluginMetadata("record")]
            public record class AnnotatedRecordPlugin : IPlugin
            {
                public void Execute() { }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("RecordPlugin", GetLocationText(diagnostic));
    }

    [Fact]
    public async Task StructsAndRecordStructsAreNotValidated()
    {
        var diagnostics = await AnalyzeAsync("""
            using Payments.Contracts;

            public struct StructPlugin : IPlugin { public void Execute() { } }

            public record struct RecordStructPlugin : IPlugin { public void Execute() { } }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task InheritedMetadataDoesNotSatisfyRequirement()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;
            using Annotype;

            [AttributeUsage(AttributeTargets.Class, Inherited = true)]
            public sealed class InheritableAttribute : Attribute { }

            [RequiresMetadata(typeof(InheritableAttribute))]
            public interface IContract { }

            [Inheritable]
            public class Base : IContract { }

            public class Derived : Base { }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ANNO001", diagnostic.Id);
        Assert.Equal("Derived", GetLocationText(diagnostic));
    }

    [Fact]
    public async Task GenericAndNestedImplementationsAreValidated()
    {
        var diagnostics = await AnalyzeAsync("""
            using Payments.Contracts;

            public class GenericPlugin<T> : IPlugin { public void Execute() { } }

            public static class Outer
            {
                private sealed class NestedPlugin : IPlugin { public void Execute() { } }
            }
            """);

        Assert.Equal(["GenericPlugin", "NestedPlugin"], diagnostics.Select(GetLocationText));
        Assert.All(diagnostics, static d => Assert.Equal("ANNO001", d.Id));
    }

    [Fact]
    public async Task GenericAttributeRequirementIsMatchedByConstructedType()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;
            using Annotype;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class TagAttribute<T> : Attribute { }

            [RequiresMetadata(typeof(TagAttribute<int>))]
            public interface ITagged { }

            [Tag<int>]
            public class Good : ITagged { }

            [Tag<string>]
            public class Bad : ITagged { }
            """);

        Assert.Equal("Bad", GetLocationText(Assert.Single(diagnostics)));
    }

    [Fact]
    public async Task AbstractBaseClassContractIsEnforcedOnConcreteDescendants()
    {
        var diagnostics = await AnalyzeAsync("""
            using Annotype;
            using Payments.Contracts;

            [RequiresMetadata(typeof(PluginMetadataAttribute))]
            public abstract class PluginBase { }

            public abstract class Intermediate : PluginBase { }

            [PluginMetadata("ok")]
            public sealed class Annotated : Intermediate { }

            public sealed class Missing : Intermediate { }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ANNO001", diagnostic.Id);
        Assert.Equal("Missing", GetLocationText(diagnostic));
        Assert.Equal(
            "Type 'Missing' derives from 'PluginBase' but is missing required attribute 'Payments.Contracts.PluginMetadataAttribute'",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ConcreteContractClassMustSatisfyItsOwnRequirement()
    {
        var diagnostics = await AnalyzeAsync("""
            using Annotype;
            using Payments.Contracts;

            [RequiresMetadata(typeof(PluginMetadataAttribute))]
            public class SelfContract { }

            [PluginMetadata("child")]
            public class Child : SelfContract { }

            public class GrandChild : Child { }
            """);

        Assert.Collection(
            diagnostics,
            d => Assert.Equal(
                "Type 'SelfContract' is marked with [RequiresMetadata] but is missing required attribute 'Payments.Contracts.PluginMetadataAttribute'",
                d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)),
            d => Assert.Equal(
                "Type 'GrandChild' derives from 'SelfContract' but is missing required attribute 'Payments.Contracts.PluginMetadataAttribute'",
                d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task ClassAndInterfaceRequiringSameAttributeReportOneError()
    {
        var diagnostics = await AnalyzeAsync("""
            using Annotype;
            using Payments.Contracts;

            [RequiresMetadata(typeof(PluginMetadataAttribute))]
            public abstract class PluginBase : IPlugin
            {
                public void Execute() { }
            }

            public sealed class Missing : PluginBase { }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(
            "Type 'Missing' implements 'Payments.Contracts.IPlugin' and derives from 'PluginBase' but is missing required attribute 'Payments.Contracts.PluginMetadataAttribute'",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task GenericBaseClassContractIsEnforced()
    {
        var diagnostics = await AnalyzeAsync("""
            using Annotype;
            using Payments.Contracts;

            [RequiresMetadata(typeof(PluginMetadataAttribute))]
            public abstract class PluginBase<T> { }

            public sealed class Missing : PluginBase<int> { }
            """);

        Assert.Equal("Missing", GetLocationText(Assert.Single(diagnostics)));
    }

    [Fact]
    public async Task InvalidClassContractReportsAnno002()
    {
        var diagnostics = await AnalyzeAsync("""
            using Annotype;

            [RequiresMetadata(typeof(string))]
            public abstract class Broken { }

            public sealed class Implementation : Broken { }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ANNO002", diagnostic.Id);
        Assert.Equal("RequiresMetadata(typeof(string))", GetLocationText(diagnostic));
    }

    [Fact]
    public async Task ClassContractFromReferencedAssemblyIsEnforced()
    {
        var contracts = TestCompilation.ToReference(TestCompilation.Create(Contracts + """

            namespace Payments.Contracts
            {
                [Annotype.RequiresMetadata(typeof(PluginMetadataAttribute))]
                public abstract class PluginBase { }
            }
            """, "Payments.Contracts"));
        var compilation = TestCompilation.Create("""
            using Payments.Contracts;

            [PluginMetadata("ok")]
            public sealed class Valid : PluginBase { }

            public sealed class Missing : PluginBase { }
            """, "Payments.Plugins", [contracts]);

        var diagnostics = await TestCompilation.GetAnalyzerDiagnosticsAsync(compilation);

        Assert.Equal("Missing", GetLocationText(Assert.Single(diagnostics)));
    }

    [Fact]
    public async Task CompilationWithoutAnnotypeContractsReportsNothing()
    {
        var diagnostics = await AnalyzeAsync("""
            public interface IUnrelated { }
            public class Unrelated : IUnrelated { }
            """);

        Assert.Empty(diagnostics);
    }
}
