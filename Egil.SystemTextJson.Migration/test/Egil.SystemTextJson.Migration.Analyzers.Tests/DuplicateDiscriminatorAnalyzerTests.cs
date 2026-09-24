using Microsoft.CodeAnalysis;

namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

public sealed class DuplicateDiscriminatorAnalyzerTests
{
    [Fact]
    public async Task Explicit_value_matching_another_sources_default_full_name_reports_STJM0001()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { public string? TypeDiscriminator { get; set; } }
            }

            namespace Models
            {
                [Egil.SystemTextJson.Migration.JsonMigratable]
                public sealed class Old { }

                [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "Models.Old")]
                public sealed class Other { }

                [Egil.SystemTextJson.Migration.JsonMigratable] public sealed class Current :
                    Egil.SystemTextJson.Migration.IMigrateFrom<Old, Current>,
                    Egil.SystemTextJson.Migration.IMigrateFrom<Other, Current> { }
            }
            """);

        var diagnostic = Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer()));

        Assert.Equal("STJM0001", diagnostic.Id);
        Assert.Contains("Models.Old", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Referenced_sources_report_a_duplicate_without_a_source_location()
    {
        var library = AnalyzerTestHelper.CreateCompilation(RegressionContracts + """
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "same")] public class First { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "same")] public class Second { }
            """).WithAssemblyName("HistoricalModels");
        using var image = new MemoryStream();
        var emitted = library.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        var compilation = AnalyzerTestHelper.CreateCompilation("""
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<First, Current>,
                Egil.SystemTextJson.Migration.IMigrateFrom<Second, Current> { }
            """).AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostic = Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer()));
        Assert.Equal("STJM0001", diagnostic.Id);
        Assert.True(diagnostic.Location.IsInSource);
    }

    [Fact]
    public async Task Derived_selector_attribute_only_makes_its_source_unknown()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration { public interface IMigrate<TS,TT>{} public interface IMigrateFrom<TS,TT>{} public sealed class JsonMigratableAttribute:System.Attribute { public string TypeDiscriminator {get;set;} } public class SchemaAttribute:System.Attribute{} public class DerivedSchemaAttribute:SchemaAttribute{} public class JsonMigrationBuilder { public void GetTypeDiscriminatorFrom<T>(System.Func<T,string> value){} } }
            [Egil.SystemTextJson.Migration.DerivedSchema][Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator="x")] public class Unknown { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator="x")] public class KnownOne { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator="x")] public class KnownTwo { }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Target:Egil.SystemTextJson.Migration.IMigrateFrom<Unknown,Target>,Egil.SystemTextJson.Migration.IMigrateFrom<KnownOne,Target>,Egil.SystemTextJson.Migration.IMigrateFrom<KnownTwo,Target>{} public class Setup { public void Go(Egil.SystemTextJson.Migration.JsonMigrationBuilder b)=>b.GetTypeDiscriminatorFrom<Egil.SystemTextJson.Migration.SchemaAttribute>(x=>"x"); }
            """);
        Assert.Equal("STJM0001", Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer())).Id);
    }

    [Fact]
    public async Task Constant_property_name_preserves_duplicate_detection()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration { public interface IMigrate<TS,TT>{} public interface IMigrateFrom<TS,TT>{} public sealed class JsonMigratableAttribute:System.Attribute { public string TypeDiscriminator {get;set;} } public class JsonMigrationBuilder { public void SetTypeDiscriminatorPropertyName(string value){} } }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator="x")] public class One{} [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator="x")] public class Two{} [Egil.SystemTextJson.Migration.JsonMigratable] public class Target:Egil.SystemTextJson.Migration.IMigrateFrom<One,Target>,Egil.SystemTextJson.Migration.IMigrateFrom<Two,Target>{} public class Setup { public void Go(Egil.SystemTextJson.Migration.JsonMigrationBuilder b)=>b.SetTypeDiscriminatorPropertyName("kind"); }
            """);
        Assert.Equal("STJM0001", Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer())).Id);
    }

    [Fact]
    public async Task Explicit_property_and_builder_default_property_do_not_report_a_collision()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration { public interface IMigrate<TS,TT>{} public interface IMigrateFrom<TS,TT>{} public sealed class JsonMigratableAttribute:System.Attribute { public string TypeDiscriminator {get;set;} public string TypeDiscriminatorPropertyName {get;set;} } public class JsonMigrationBuilder { public void SetTypeDiscriminatorPropertyName(string value){} } }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator="x",TypeDiscriminatorPropertyName="$type")] public class Explicit { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator="x")] public class Default { }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Target:Egil.SystemTextJson.Migration.IMigrateFrom<Explicit,Target>,Egil.SystemTextJson.Migration.IMigrateFrom<Default,Target>{} public class Setup { public void Go(Egil.SystemTextJson.Migration.JsonMigrationBuilder b)=>b.SetTypeDiscriminatorPropertyName("kind"); }
            """);

        Assert.Empty(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer()));
    }

    [Fact]
    public async Task Two_default_properties_still_report_a_collision_after_builder_configuration()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration { public interface IMigrate<TS,TT>{} public interface IMigrateFrom<TS,TT>{} public sealed class JsonMigratableAttribute:System.Attribute { public string TypeDiscriminator {get;set;} public string TypeDiscriminatorPropertyName {get;set;} } public class JsonMigrationBuilder { public void SetTypeDiscriminatorPropertyName(string value){} } }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator="x")] public class One { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator="x")] public class Two { }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Target:Egil.SystemTextJson.Migration.IMigrateFrom<One,Target>,Egil.SystemTextJson.Migration.IMigrateFrom<Two,Target>{} public class Setup { public void Go(Egil.SystemTextJson.Migration.JsonMigrationBuilder b)=>b.SetTypeDiscriminatorPropertyName("kind"); }
            """);

        var diagnostic = Assert.Single(await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer()));
        Assert.Equal("STJM0001", diagnostic.Id);
        Assert.Contains("configured default", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Repeated_inherited_and_dual_contracts_do_not_report_a_collision()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration { public interface IMigrate<TS,TT>{} public interface IMigrateFrom<TS,TT>{} public sealed class JsonMigratableAttribute:System.Attribute { public string TypeDiscriminator {get;set;} } }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator="same")] public class Old { }
            public class Base : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Target> { }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Target : Base, Egil.SystemTextJson.Migration.IMigrate<Old, Target> { }
            """);
        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer());
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Nullable_and_explicit_generic_sources_preserve_distinct_contracts()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration { public interface IMigrate<TS,TT>{} public interface IMigrateFrom<TS,TT>{} public sealed class JsonMigratableAttribute:System.Attribute { public string TypeDiscriminator {get;set;} } }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator="same")] public struct Old { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator="same")] public class Generic<T> { }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Target : Egil.SystemTextJson.Migration.IMigrateFrom<Old?,Target>, Egil.SystemTextJson.Migration.IMigrate<Old,Target>, Egil.SystemTextJson.Migration.IMigrateFrom<Generic<int>,Target> { }
            """);
        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer());
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("STJM0001", diagnostic.Id));
    }
    [Fact]
    public async Task Duplicate_source_discriminators_for_one_target_report_STJM0001()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { public string TypeDiscriminator { get; set; } public string TypeDiscriminatorPropertyName { get; set; } }
            }

            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "v1")]
            public class SourceOne { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "v1")]
            public class SourceTwo { }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Target : Egil.SystemTextJson.Migration.IMigrateFrom<SourceOne, Target>, Egil.SystemTextJson.Migration.IMigrateFrom<SourceTwo, Target> { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer());

        Assert.Equal("STJM0001", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Different_targets_and_property_names_do_not_report_STJM0001()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { public string TypeDiscriminator { get; set; } public string TypeDiscriminatorPropertyName { get; set; } }
            }

            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "same")]
            public class SourceOne { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "same", TypeDiscriminatorPropertyName = "kind")]
            public class SourceTwo { }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class FirstTarget : Egil.SystemTextJson.Migration.IMigrateFrom<SourceOne, FirstTarget> { }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class SecondTarget : Egil.SystemTextJson.Migration.IMigrateFrom<SourceOne, SecondTarget>, Egil.SystemTextJson.Migration.IMigrateFrom<SourceTwo, SecondTarget> { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Runtime_selector_sources_are_unknown_without_suppressing_known_collisions()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation("""
            namespace Egil.SystemTextJson.Migration
            {
                public interface IMigrate<TSource, TTarget> { }
                public interface IMigrateFrom<TSource, TTarget> { }
                public sealed class JsonMigratableAttribute : System.Attribute { public string TypeDiscriminator { get; set; } }
                public sealed class SchemaAttribute : System.Attribute { }
                public sealed class JsonMigrationBuilder { public void GetTypeDiscriminatorFrom<T>(System.Func<T, string> selector) { } }
            }
            [Egil.SystemTextJson.Migration.Schema]
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "same")]
            public class UnknownSource { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "same")]
            public class KnownSourceOne { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "same")]
            public class KnownSourceTwo { }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Target : Egil.SystemTextJson.Migration.IMigrateFrom<UnknownSource, Target>, Egil.SystemTextJson.Migration.IMigrateFrom<KnownSourceOne, Target>, Egil.SystemTextJson.Migration.IMigrateFrom<KnownSourceTwo, Target> { }
            public class Setup { public void Configure(Egil.SystemTextJson.Migration.JsonMigrationBuilder builder) => builder.GetTypeDiscriminatorFrom<Egil.SystemTextJson.Migration.SchemaAttribute>(value => "runtime"); }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer());

        Assert.Equal("STJM0001", Assert.Single(diagnostics).Id);
    }


    private const string RegressionContracts = """
        namespace Egil.SystemTextJson.Migration
        {
            public interface IMigrate<TSource, TTarget> { }
            public interface IMigrateFrom<TSource, TTarget> { }
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, Inherited = true)]
            public sealed class JsonMigratableAttribute : System.Attribute
            {
                public string TypeDiscriminator { get; set; }
            }
        }
        """;

    [Theory]
    [InlineData("int[]")]
    [InlineData("T?")]
    public async Task Array_and_unresolved_nullable_sources_do_not_crash_the_analyzer(string sourceType)
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(RegressionContracts + $$"""
            [Egil.SystemTextJson.Migration.JsonMigratable]
            public class Current<T> : Egil.SystemTextJson.Migration.IMigrateFrom<{{sourceType}}, Current<T>> where T : struct { }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("IMigrateFrom")]
    [InlineData("IMigrate")]
    public async Task Unmarked_target_does_not_have_a_runtime_discriminator_map(string contract)
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(RegressionContracts + $$"""
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "same")] public class First { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "same")] public class Second { }
            public class Current { }
            public class Contracts : Egil.SystemTextJson.Migration.{{contract}}<First, Current>, Egil.SystemTextJson.Migration.{{contract}}<Second, Current> { }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData(", Egil.SystemTextJson.Migration.IMigrateFrom<Second, Current>", "")]
    [InlineData("", "public class External : Egil.SystemTextJson.Migration.IMigrate<Second, Current> { }")]
    public async Task Inherited_target_marker_still_checks_static_and_external_source_pairs(string targetContract, string externalDeclaration)
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(RegressionContracts + $$"""
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "same")] public class First { }
            [Egil.SystemTextJson.Migration.JsonMigratable(TypeDiscriminator = "same")] public class Second { }
            [Egil.SystemTextJson.Migration.JsonMigratable] public class Base { }
            public class Current : Base, Egil.SystemTextJson.Migration.IMigrateFrom<First, Current>{{targetContract}} { }
            {{externalDeclaration}}
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new DuplicateDiscriminatorAnalyzer());

        Assert.Equal("STJM0001", Assert.Single(diagnostics).Id);
    }
}
