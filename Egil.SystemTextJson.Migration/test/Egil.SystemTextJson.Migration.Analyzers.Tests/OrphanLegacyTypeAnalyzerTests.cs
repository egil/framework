using Microsoft.CodeAnalysis;
using System.Globalization;

namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

public sealed class OrphanLegacyTypeAnalyzerTests
{
    [Fact]
    public async Task Marked_legacy_type_without_a_visible_source_contract_reports_STJM0011()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Old { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new OrphanLegacyTypeAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("STJM0011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("MigratedExternally = true", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("remove the marker", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Direct_static_and_external_source_contracts_prevent_STJM0011()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class StaticOld { }
            public class StaticCurrent : Egil.SystemTextJson.Migration.IMigrateFrom<StaticOld, StaticCurrent> { }

            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class ExternalOld { }
            public class ExternalCurrent { }
            public class ExternalMigrator : Egil.SystemTextJson.Migration.IMigrate<ExternalOld, ExternalCurrent> { }

            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public struct StructOld { }
            public class StructCurrent : Egil.SystemTextJson.Migration.IMigrateFrom<StructOld, StructCurrent> { }

            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public enum EnumOld { Value }
            public class EnumCurrent { }
            public class EnumMigrator : Egil.SystemTextJson.Migration.IMigrate<EnumOld, EnumCurrent> { }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new OrphanLegacyTypeAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Marked_struct_and_enum_without_a_visible_source_contract_report_STJM0011()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public struct OldStruct { }
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public enum OldEnum { Value }
            """);

        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new OrphanLegacyTypeAnalyzer());

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("STJM0011", diagnostic.Id));
    }

    [Fact]
    public async Task Migrated_externally_suppresses_only_STJM0011_when_the_migrator_is_in_another_assembly()
    {
        var legacyCompilation = AnalyzerTestHelper.CreateCompilation(Contracts + """
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType(MigratedExternally = true)]
            public class Old { }
            public class OrdinaryUse { public Old Value { get; set; } = new(); }
            """).WithAssemblyName("HistoricalPayloads");
        using var image = new MemoryStream();
        var emitted = legacyCompilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        var externalMigrator = AnalyzerTestHelper.CreateCompilation("""
            public class Current { }
            public class Migrator : Egil.SystemTextJson.Migration.IMigrate<Old, Current> { }
            """).AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
        Assert.Empty(externalMigrator.GetDiagnostics(TestContext.Current.CancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var orphanDiagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(legacyCompilation, new OrphanLegacyTypeAnalyzer());
        var usageDiagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(legacyCompilation, new LegacyTypeUsageAnalyzer());

        Assert.Empty(orphanDiagnostics);
        Assert.Contains(usageDiagnostics, diagnostic => diagnostic.Id == "STJM0010");
    }

    private const string Contracts = """
        namespace Egil.SystemTextJson.Migration
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Enum)]
            public sealed class JsonMigrationLegacyTypeAttribute : System.Attribute { public bool MigratedExternally { get; set; } }
            public sealed class JsonMigratableAttribute : System.Attribute { }
            public interface IMigrateFrom<TSource, TTarget> { }
            public interface IMigrate<TSource, TTarget> { }
        }
        """;

}
