using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Egil.SystemTextJson.Migration.Analyzers.Tests;

public sealed class LegacyTypeUsageAnalyzerTests
{
    private const string Contracts = """
        namespace Egil.SystemTextJson.Migration
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Enum)]
            public sealed class JsonMigrationLegacyTypeAttribute : System.Attribute { public bool MigratedExternally { get; set; } }
            public sealed class JsonMigratableAttribute : System.Attribute { public System.Type UndiscriminatedSourceType { get; set; } }
            public interface IMigrateFrom<TSource, TTarget> { static abstract bool TryMigrateFrom(TSource source, out TTarget target); }
            public interface IMigrate<TSource, TTarget> { bool TryMigrateFrom(TSource source, out TTarget target); }
            public sealed class JsonMigrationBuilder { public void RegisterMigrator<TSource, TTarget, TMigrator>() { } public void RegisterMigrator<TMigrator>() { } }
        }
        """;
    private const string SourceType = """
        [Egil.SystemTextJson.Migration.JsonMigrationLegacyType] public class Old { public int Value { get; set; } }
        """;

    [Fact]
    public async Task Ordinary_replacement_members_and_helpers_warn()
    {
        await VerifyAsync("""
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Current>
            {
                public [|Old|] Value { get; set; }
                public Current([|Old|] value) { [|Value|] = [|value|]; }
                public static bool TryMigrateFrom(Old source, out Current target) { target = new Current(source); return true; }
                public void Helper() { [|var|] value = new Old(); }
            }
            """);
    }

    [Fact]
    public async Task Named_creation_generic_and_inferred_uses_have_one_diagnostic_per_use()
    {
        await VerifyAsync("""
            public class Uses
            {
                public System.Collections.Generic.List<[|Old|]> Items = new();
                public void Run()
                {
                    _ = new [|Old|]();
                    [|var|] inferred = new Old();
                    _ = [|inferred|].Value;
                    _ = [|Factory.Read()|];
                }
            }
            public static class Factory { public static [|Old|] Read() => [|new()|]; }
            """);
    }

    [Fact]
    public async Task Direct_static_and_external_implementations_and_own_declaration_are_allowed()
    {
        await VerifyAsync("""
            public partial class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Current>
            {
                public static bool TryMigrateFrom(Old source, out Current target)
                {
                    var copy = source;
                    target = new Current();
                    return copy.Value > 0;
                }
            }
            public class Migrator : Egil.SystemTextJson.Migration.IMigrate<Old, Current>
            {
                bool Egil.SystemTextJson.Migration.IMigrate<Old, Current>.TryMigrateFrom(Old source, out Current target)
                {
                    var copy = source;
                    target = new Current();
                    return copy.Value > 0;
                }
            }
            """);
    }

    [Fact]
    public async Task Generic_source_in_direct_migration_method_is_allowed()
    {
        await VerifyAsync("""
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<Old>, Current>
            {
                public static bool TryMigrateFrom(System.Collections.Generic.List<Old> source, out Current target)
                {
                    System.Collections.Generic.List<Old> copy = source;
                    target = new Current();
                    return copy.Count > 0;
                }
            }
            """);
    }

    [Fact]
    public async Task Direct_chain_does_not_allow_unrelated_legacy_types_or_helper_methods()
    {
        await VerifyAsync("""
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType] public class Older { }
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Current>
            {
                public static bool TryMigrateFrom(Old source, out Current target)
                {
                    _ = new [|Older|](); target = new(); return true;
                }
                public static void Helper([|Old|] source) { }
            }
            """);
    }

    [Theory]
    [InlineData("IMigrateFrom", "public static")]
    [InlineData("IMigrate", "public")]
    public async Task Migration_edge_can_chain_through_a_reachable_legacy_type(string contract, string modifiers)
    {
        await VerifyAsync($$"""
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Middle : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Middle>
            {
                public static bool TryMigrateFrom(Old source, out Middle target) { target = new(); return true; }
            }
            [Egil.SystemTextJson.Migration.JsonMigratable(UndiscriminatedSourceType = typeof(Old))]
            public class Current : Egil.SystemTextJson.Migration.{{contract}}<Old, Current>, Egil.SystemTextJson.Migration.{{contract}}<Middle, Current>
            {
                {{modifiers}} bool TryMigrateFrom(Old source, out Current target)
                {
                    if (Middle.TryMigrateFrom(source, out var middle))
                    {
                        Middle copy = middle;
                        return TryMigrateFrom(copy, out target);
                    }
                    target = null; return false;
                }
                {{modifiers}} bool TryMigrateFrom(Middle source, out Current target) { target = new(); return true; }
            }
            """);
    }

    [Fact]
    public async Task Json_metadata_and_required_registration_references_are_allowed()
    {
        await VerifyAsync("""
            [System.Text.Json.Serialization.JsonSerializable(typeof(Old))]
            public class Metadata { }
            [Egil.SystemTextJson.Migration.JsonMigratable(UndiscriminatedSourceType = typeof(Old))]
            public class Current { }
            public class Setup
            {
                public void Register(Egil.SystemTextJson.Migration.JsonMigrationBuilder builder)
                {
                    builder.RegisterMigrator<Old, Current, Setup>();
                    builder.RegisterMigrator<System.Collections.Generic.List<Old>, Current, Setup>();
                    builder.RegisterMigrator<[|Old|]>();
                    builder.RegisterMigrator<Current, [|Old|], Setup>();
                    builder.RegisterMigrator<Current, Current, [|Old|]>();
                    builder.RegisterMigrator<Old, [|Old|], [|Old|]>();
                    _ = typeof([|Old|]);
                }
            }
            """);
    }

    [Theory]
    [InlineData("IMigrateFrom", "public static")]
    [InlineData("IMigrate", "public")]
    public async Task Migration_edge_can_chain_through_multiple_legacy_hops(string contract, string modifiers)
    {
        await VerifyAsync($$"""
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Middle : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Middle>
            {
                public static bool TryMigrateFrom(Old source, out Middle target) { target = new(); return true; }
            }
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Last : Egil.SystemTextJson.Migration.IMigrateFrom<Middle, Last>
            {
                public static bool TryMigrateFrom(Middle source, out Last target) { target = new(); return true; }
            }
            public class Current : Egil.SystemTextJson.Migration.{{contract}}<Old, Current>, Egil.SystemTextJson.Migration.{{contract}}<Last, Current>
            {
                {{modifiers}} bool TryMigrateFrom(Old source, out Current target)
                {
                    if (Middle.TryMigrateFrom(source, out var middle) && Last.TryMigrateFrom(middle, out var last))
                    {
                        return TryMigrateFrom(last, out target);
                    }
                    target = null; return false;
                }
                {{modifiers}} bool TryMigrateFrom(Last source, out Current target) { target = new(); return true; }
            }
            """);
    }

    [Fact]
    public async Task Reachable_intermediate_types_still_warn_in_unrelated_members_and_nested_helpers()
    {
        await VerifyAsync("""
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Middle : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Middle>
            {
                public static bool TryMigrateFrom(Old source, out Middle target) { target = new(); return true; }
            }
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Current>
            {
                public [|Middle|] Field;
                public [|Middle|] Value { get; set; }
                public Current([|Middle|] value) { }
                public static void Helper([|Middle|] value) { }
                public static bool TryMigrateFrom(int source, out Current target) { _ = new [|Middle|](); target = null; return false; }
                public static bool TryMigrateFrom(Old source, out Current target)
                {
                    _ = new Middle();
                    static void Local() { _ = new [|Middle|](); }
                    System.Action action = () => { _ = new [|Middle|](); };
                    target = null; return false;
                }
            }
            """);
    }

    [Theory]
    [InlineData("Old", "Last")]
    [InlineData("Other", "[|Last|]")]
    public async Task Cyclic_paths_only_allow_intermediates_when_the_edge_source_is_reachable(string source, string use)
    {
        await VerifyAsync($$"""
            public class Other { }
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Middle : Egil.SystemTextJson.Migration.IMigrateFrom<Last, Middle>, Egil.SystemTextJson.Migration.IMigrateFrom<{{source}}, Middle>
            {
                public static bool TryMigrateFrom(Last source, out Middle target) { target = new(); return true; }
                public static bool TryMigrateFrom({{source}} source, out Middle target) { target = new(); return true; }
            }
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Last : Egil.SystemTextJson.Migration.IMigrateFrom<Middle, Last>
            {
                public static bool TryMigrateFrom(Middle source, out Last target) { target = new(); return true; }
            }
            public class Current : Egil.SystemTextJson.Migration.IMigrate<Old, Current>
            {
                bool Egil.SystemTextJson.Migration.IMigrate<Old, Current>.TryMigrateFrom(Old source, out Current target)
                {
                    _ = new {{use}}(); target = new(); return true;
                }
            }
            """);
    }

    [Fact]
    public async Task Reachability_preserves_constructed_generic_source_and_target_identity()
    {
        await VerifyAsync("""
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Version<T> { }
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Middle<T> : Egil.SystemTextJson.Migration.IMigrateFrom<Version<T>, Middle<T>>
            {
                public static bool TryMigrateFrom(Version<T> source, out Middle<T> target) { target = new(); return true; }
            }
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class WrongTarget<T> : Egil.SystemTextJson.Migration.IMigrateFrom<Version<T>, Middle<T>>
            {
                public static bool TryMigrateFrom(Version<T> source, out Middle<T> target) { target = new(); return true; }
            }
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Version<int>, Current>
            {
                public static bool TryMigrateFrom(Version<int> source, out Current target)
                {
                    _ = new Middle<int>();
                    _ = new [|Middle<string>|]();
                    _ = new [|WrongTarget<int>|]();
                    target = new(); return true;
                }
            }
            """);
    }

    [Fact]
    public async Task An_array_source_reaches_array_migrators_but_not_element_migrators()
    {
        await VerifyAsync("""
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Batch : Egil.SystemTextJson.Migration.IMigrateFrom<Old[], Batch>
            {
                public static bool TryMigrateFrom(Old[] source, out Batch target) { target = new(); return true; }
            }
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Single : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Single>
            {
                public static bool TryMigrateFrom(Old source, out Single target) { target = new(); return true; }
            }
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Old[], Current>
            {
                public static bool TryMigrateFrom(Old[] source, out Current target)
                {
                    Batch.TryMigrateFrom(source, out var batch);
                    _ = new [|Single|](); target = new(); return true;
                }
            }
            """);
    }

    [Fact]
    public async Task Unmarked_hops_and_foreign_migration_contracts_do_not_grant_reachability()
    {
        await VerifyAsync("""
            public class Unmarked : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Unmarked>
            {
                public static bool TryMigrateFrom(Old source, out Unmarked target) { target = new(); return true; }
            }
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Last : Egil.SystemTextJson.Migration.IMigrateFrom<Unmarked, Last>
            {
                public static bool TryMigrateFrom(Unmarked source, out Last target) { target = new(); return true; }
            }
            public interface IMigrateFrom<TSource, TTarget> { }
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Foreign : IMigrateFrom<[|Old|], [|Foreign|]> { }
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Current>
            {
                public static bool TryMigrateFrom(Old source, out Current target)
                {
                    _ = new [|Last|](); _ = new [|Foreign|](); target = new(); return true;
                }
            }
            """);
    }

    [Fact(Timeout = 10000)]
    public async Task Expanding_generic_paths_warn_when_the_source_cannot_be_reached()
    {
        await VerifyAsync("""
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Loop<T> : Egil.SystemTextJson.Migration.IMigrateFrom<Loop<System.Collections.Generic.List<T>>, Loop<T>>
            {
                public static bool TryMigrateFrom(Loop<System.Collections.Generic.List<T>> source, out Loop<T> target) { target = new(); return true; }
            }
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Current>
            {
                public static bool TryMigrateFrom(Old source, out Current target)
                {
                    _ = new [|Loop<int>|](); target = new(); return true;
                }
            }
            """);
    }

    [Fact]
    public async Task Finite_paths_can_repeat_generic_definitions_with_different_arguments()
    {
        await VerifyAsync("""
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Box<T> : Egil.SystemTextJson.Migration.IMigrateFrom<T, Box<T>>
            {
                public static bool TryMigrateFrom(T source, out Box<T> target) { target = new(); return true; }
            }
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Loop<T> : Egil.SystemTextJson.Migration.IMigrateFrom<Loop<System.Collections.Generic.List<T>>, Loop<T>>,
                Egil.SystemTextJson.Migration.IMigrateFrom<T, Loop<T>>
            {
                public static bool TryMigrateFrom(Loop<System.Collections.Generic.List<T>> source, out Loop<T> target) { target = new(); return true; }
                public static bool TryMigrateFrom(T source, out Loop<T> target) { target = new(); return true; }
            }
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Current>,
                Egil.SystemTextJson.Migration.IMigrateFrom<System.Collections.Generic.List<System.Collections.Generic.List<int>>, Current>
            {
                public static bool TryMigrateFrom(Old source, out Current target)
                {
                    _ = new Box<Box<Box<Old>>>(); target = new(); return true;
                }
                public static bool TryMigrateFrom(System.Collections.Generic.List<System.Collections.Generic.List<int>> source, out Current target)
                {
                    _ = new Loop<int>(); target = new(); return true;
                }
            }
            """);
    }

    [Fact]
    public async Task Same_named_foreign_attributes_and_registration_methods_do_not_exempt_usage()
    {
        await VerifyAsync("""
            public sealed class JsonSerializableAttribute : System.Attribute { public JsonSerializableAttribute(System.Type type) { } }
            [JsonSerializable(typeof([|Old|]))] public class Metadata { }
            public class Setup
            {
                public void RegisterMigrator<TSource, TTarget, TMigrator>() { }
                public void Run() { RegisterMigrator<[|Old|], Setup, Setup>(); }
            }
            """);
    }

    [Fact]
    public async Task Tests_and_external_migration_flag_do_not_suppress_usage()
    {
        await VerifyAsync("""
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType(MigratedExternally = true)] public struct OldStruct { }
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType] public enum OldEnum { Value }
            public class MigrationTests
            {
                public void Run() { _ = new [|OldStruct|](); _ = [|OldEnum|].Value; }
            }
            """);
    }

    [Fact]
    public async Task Marker_on_referenced_source_is_recognized()
    {
        var library = AnalyzerTestHelper.CreateCompilation(Contracts + SourceType).WithAssemblyName("HistoricalContracts");
        using var image = new MemoryStream();
        var emitted = library.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var reference = MetadataReference.CreateFromImage(image.ToArray());

        await VerifyAsync("public class Consumer { public [|Old|] Value { get; set; } }", reference: reference);
    }

    [Fact]
    public async Task Inferred_member_and_foreach_values_warn_without_duplicate_receiver_warnings()
    {
        await VerifyAsync("""
            public class Holder { public [|Old|] Value => null; public [|Old|][] Values => null; }
            public class Uses
            {
                public void Run(Holder holder)
                {
                    _ = [|holder.Value|];
                    _ = holder?[|.Value|];
                    [|var|] value = holder.Value;
                    foreach ([|var|] item in [|holder.Values|]) { _ = [|item|].Value; }
                }
            }
            """);
    }

    [Fact]
    public async Task Own_declaration_is_allowed_but_inheritance_does_not_propagate_the_marker()
    {
        await VerifyAsync("""
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Recursive { public Recursive Next { get; set; } public Recursive Copy() => new Recursive(); }
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class GenericLegacy<T> { public GenericLegacy<int> Other { get; set; } }
            public class Derived : [|Recursive|] { }
            public class Uses { public Derived Value { get; set; } }
            """);
    }

    [Fact]
    public async Task Local_helpers_inside_direct_migration_are_not_blanket_exempt()
    {
        await VerifyAsync("""
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Current>
            {
                public static bool TryMigrateFrom(Old source, out Current target)
                {
                    static int Read([|Old|] value) => [|value|].Value;
                    target = new(); return Read(source) > 0;
                }
            }
            """);
    }
    [Fact]
    public async Task Chain_exemptions_only_cover_the_implemented_edge()
    {
        await VerifyAsync("""
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class Middle : Egil.SystemTextJson.Migration.IMigrateFrom<Old, Middle>
            {
                public static bool TryMigrateFrom(Old source, out Middle target) { target = new(); return true; }
            }
            public class Current : Egil.SystemTextJson.Migration.IMigrateFrom<Middle, Current>
            {
                public static bool TryMigrateFrom(Middle source, out Current target)
                {
                    _ = new [|Old|](); target = new(); return true;
                }
            }
            """);
    }

    [Fact]
    public async Task Same_named_foreign_marker_does_not_mark_a_type()
    {
        await VerifyAsync("""
            public class JsonMigrationLegacyTypeAttribute : System.Attribute { }
            [JsonMigrationLegacyType] public class Foreign { }
            public class Uses { public Foreign Value = new(); }
            """);
    }
    [Fact]
    public async Task Marked_nested_type_qualifier_warns_once()
    {
        await VerifyAsync("""
            [Egil.SystemTextJson.Migration.JsonMigrationLegacyType]
            public class LegacyContainer { public class Nested { } }
            public class Uses
            {
                public [|LegacyContainer|].Nested Value { get; set; }
                public void Run() { _ = new [|LegacyContainer|].Nested(); }
            }
            """);
    }
    private static async Task VerifyAsync(string markedSource, MetadataReference? reference = null)
    {
        var spans = new List<TextSpan>();
        var source = reference is null ? Contracts + SourceType + markedSource : markedSource;
        while (source.IndexOf("[|", StringComparison.Ordinal) is var start && start >= 0)
        {
            source = source.Remove(start, 2);
            var end = source.IndexOf("|]", start, StringComparison.Ordinal);
            source = source.Remove(end, 2);
            spans.Add(TextSpan.FromBounds(start, end));
        }

        var compilation = AnalyzerTestHelper.CreateCompilation(source);
        if (reference is not null)
        {
            compilation = compilation.AddReferences(reference);
        }

        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
        var diagnostics = await AnalyzerTestHelper.GetAnalyzerDiagnosticsAsync(compilation, new LegacyTypeUsageAnalyzer());
        Assert.All(diagnostics, diagnostic => Assert.Equal("STJM0010", diagnostic.Id));
        Assert.Equal(spans.OrderBy(span => span.Start), diagnostics.Select(diagnostic => diagnostic.Location.SourceSpan));
    }
}
