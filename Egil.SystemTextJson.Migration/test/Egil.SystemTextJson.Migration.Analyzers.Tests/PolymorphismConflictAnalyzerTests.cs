namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

public sealed class PolymorphismConflictAnalyzerTests
{
    [Fact]
    public async Task Reports_once_when_a_migratable_base_type_gains_polymorphism_from_a_directly_implemented_interface()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            using System.Text.Json.Serialization;
            namespace Egil.SystemTextJson.Migration { public interface IMigrate<TSource,TTarget>{} public interface IMigrateFrom<TSource,TTarget>{} public sealed class JsonMigratableAttribute:System.Attribute{} }
            [JsonPolymorphic]
            [JsonDerivedType(typeof(Derived))]
            public interface IPolymorphic { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Base { }
            public class Derived : Base, IPolymorphic { }
            public class Descendant : Derived { }
            """);

        var diagnostic = Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new PolymorphismConflictAnalyzer()));
        var sourceText = await diagnostic.Location.SourceTree!.GetTextAsync(TestContext.Current.CancellationToken);

        Assert.Equal("STJM0005", diagnostic.Id);
        Assert.Equal("Derived", sourceText.ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task Reports_when_a_migratable_base_type_gains_polymorphism_from_an_inherited_interface()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            using System.Text.Json.Serialization;
            namespace Egil.SystemTextJson.Migration { public interface IMigrate<TSource,TTarget>{} public interface IMigrateFrom<TSource,TTarget>{} public sealed class JsonMigratableAttribute:System.Attribute{} }
            [JsonPolymorphic]
            [JsonDerivedType(typeof(Derived))]
            public interface IPolymorphic { }
            public interface IChild : IPolymorphic { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Base { }
            public class Derived : Base, IChild { }
            """);

        var diagnostic = Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new PolymorphismConflictAnalyzer()));
        var sourceText = await diagnostic.Location.SourceTree!.GetTextAsync(TestContext.Current.CancellationToken);

        Assert.Equal("STJM0005", diagnostic.Id);
        Assert.Equal("Derived", sourceText.ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task Reports_a_conflict_when_a_migratable_type_implements_a_polymorphic_interface()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            using System.Text.Json.Serialization;
            namespace Egil.SystemTextJson.Migration { public interface IMigrate<TSource,TTarget>{} public interface IMigrateFrom<TSource,TTarget>{} public sealed class JsonMigratableAttribute:System.Attribute{} }
            [JsonPolymorphic]
            [JsonDerivedType(typeof(MigratableCase))]
            public interface ICase { }
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public sealed class MigratableCase : ICase { }
            public interface IUnrelated { }
            """);

        Assert.Equal("STJM0005", Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new PolymorphismConflictAnalyzer())).Id);
    }

    [Fact]
    public async Task Reports_a_conflict_when_a_migratable_type_is_directly_polymorphic()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            using System.Text.Json.Serialization;

            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }

            [Egil.SystemTextJson.Migration.JsonMigratable]
            [JsonPolymorphic]
            public class DirectConflict { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new PolymorphismConflictAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("STJM0005", diagnostic.Id);
        Assert.Equal("https://github.com/egil/framework/blob/main/Egil.SystemTextJson.Migration/docs/recipes/polymorphism.md", diagnostic.Descriptor.HelpLinkUri);
    }

    [Fact]
    public async Task Reports_a_conflict_when_a_migratable_type_directly_declares_a_derived_type()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            using System.Text.Json.Serialization;

            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }

            [Egil.SystemTextJson.Migration.JsonMigratable]
            [JsonDerivedType(typeof(DerivedType))]
            public class DerivedTypeBase { }

            public class DerivedType : DerivedTypeBase { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new PolymorphismConflictAnalyzer());

        Assert.Equal("STJM0005", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Reports_a_conflict_when_a_migratable_derived_type_inherits_polymorphism()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            using System.Text.Json.Serialization;

            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }

            [JsonPolymorphic]
            [JsonDerivedType(typeof(MigratableDerived))]
            public class PolymorphicBase { }

            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class MigratableDerived : PolymorphicBase { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new PolymorphismConflictAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("STJM0005", diagnostic.Id);
    }

    [Fact]
    public async Task Does_not_report_unrelated_polymorphic_and_migratable_types()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            using System.Text.Json.Serialization;

            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }

            [JsonPolymorphic]
            [JsonDerivedType(typeof(PolymorphicChild))]
            public class PolymorphicBase { }
            public class PolymorphicChild : PolymorphicBase { }

            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class MigratableType { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new PolymorphismConflictAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Analyzer_loads_for_a_compilation_that_references_migration_contracts()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { }
            }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new PolymorphismConflictAnalyzer());

        Assert.Empty(diagnostics);
    }

}
