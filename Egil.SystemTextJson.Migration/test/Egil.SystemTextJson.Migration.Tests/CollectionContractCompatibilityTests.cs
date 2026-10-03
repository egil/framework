using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Egil.SystemTextJson.Migration.Tests;

public class CollectionContractCompatibilityTests
{
    public static TheoryData<Type> EnumerableSources => new()
    {
        typeof(int[]),
        typeof(List<int>),
        typeof(IList<int>),
        typeof(ICollection<int>),
        typeof(IEnumerable<int>),
        typeof(IReadOnlyList<int>),
        typeof(IReadOnlyCollection<int>),
        typeof(ISet<int>),
        typeof(HashSet<int>),
        typeof(Queue<int>),
        typeof(Stack<int>),
        typeof(ConcurrentQueue<int>),
        typeof(ConcurrentStack<int>),
        typeof(Collection<int>),
        typeof(ImmutableArray<int>),
        typeof(ImmutableList<int>),
        typeof(ImmutableHashSet<int>),
        typeof(ImmutableQueue<int>),
        typeof(ImmutableStack<int>),
        typeof(Memory<int>),
        typeof(ReadOnlyMemory<int>),
        typeof(AlternateEnumerableList),
        typeof(AlternateEnumerableQueue),
    };

    public static TheoryData<Type> DictionarySources => new()
    {
        typeof(Dictionary<string, int>),
        typeof(IDictionary<string, int>),
        typeof(IReadOnlyDictionary<string, int>),
        typeof(SortedDictionary<string, int>),
        typeof(SortedList<string, int>),
        typeof(ConcurrentDictionary<string, int>),
        typeof(ImmutableDictionary<string, int>),
        typeof(ImmutableSortedDictionary<string, int>),
        typeof(AlternateEnumerableDictionary),
    };

    [Theory]
    [MemberData(nameof(EnumerableSources))]
    public void Numeric_enumerable_source_wins_over_boolean_source(Type sourceType)
    {
        var factory = (IOptionsFactory)Activator.CreateInstance(typeof(OptionsFactory<>).MakeGenericType(sourceType))!;
        var options = factory.CreateEnumerableOptions();
        var target = typeof(EnumerableTarget<>).MakeGenericType(sourceType);

        var result = (ISelectedSource)JsonSerializer.Deserialize("[42]", target, options)!;

        Assert.Equal("[42]", result.Payload);
    }

    [Theory]
    [MemberData(nameof(DictionarySources))]
    public void Numeric_dictionary_values_win_over_boolean_values(Type sourceType)
    {
        var factory = (IOptionsFactory)Activator.CreateInstance(typeof(OptionsFactory<>).MakeGenericType(sourceType))!;
        var options = factory.CreateDictionaryOptions();
        var target = typeof(DictionaryTarget<>).MakeGenericType(sourceType);

        var result = (ISelectedSource)JsonSerializer.Deserialize("""{"value":42}""", target, options)!;

        Assert.Equal("""{"value":42}""", result.Payload);
    }

    [Theory]
    [InlineData(typeof(ArrayList), "[42]")]
    [InlineData(typeof(Hashtable), "{\"value\":42}")]
    public void Non_generic_collection_sources_remain_readable(Type sourceType, string json)
    {
        var options = new JsonSerializerOptions().AddJsonMigrationSupport();
        var target = typeof(SingleSourceTarget<>).MakeGenericType(sourceType);

        var result = (ISelectedSource)JsonSerializer.Deserialize(json, target, options)!;

        Assert.Equal(json, result.Payload);
    }

    [Fact]
    public void Read_only_collection_source_keeps_the_serializer_deserialization_error()
    {
        var options = new OptionsFactory<ReadOnlyCollection<int>>().CreateEnumerableOptions();

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<EnumerableTarget<ReadOnlyCollection<int>>>("[42]", options));
    }

    [Fact]
    public void Read_only_dictionary_source_keeps_the_serializer_deserialization_error()
    {
        var options = new OptionsFactory<ReadOnlyDictionary<string, int>>().CreateDictionaryOptions();

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<DictionaryTarget<ReadOnlyDictionary<string, int>>>("""{"value":42}""", options));
    }

    public interface ISelectedSource
    {
        string Payload { get; }
    }

    [JsonMigratable]
    public sealed record EnumerableTarget<TSource>(string Payload)
        : ISelectedSource, IMigrateFrom<TSource, EnumerableTarget<TSource>>
    {
        public static bool TryMigrateFrom(TSource source, out EnumerableTarget<TSource> result)
        {
            result = new(JsonSerializer.Serialize(source));
            return true;
        }

    }

    [JsonMigratable]
    public sealed record DictionaryTarget<TSource>(string Payload)
        : ISelectedSource, IMigrateFrom<TSource, DictionaryTarget<TSource>>
    {
        public static bool TryMigrateFrom(TSource source, out DictionaryTarget<TSource> result)
        {
            result = new(JsonSerializer.Serialize(source));
            return true;
        }

    }

    [JsonMigratable]
    public sealed record SingleSourceTarget<TSource>(string Payload) : ISelectedSource, IMigrateFrom<TSource, SingleSourceTarget<TSource>>
    {
        public static bool TryMigrateFrom(TSource source, out SingleSourceTarget<TSource> result)
        {
            result = new(JsonSerializer.Serialize(source));
            return true;
        }
    }

    public sealed class AlternateEnumerableList : List<int>, IEnumerable<string>
    {
        IEnumerator<string> IEnumerable<string>.GetEnumerator() => Enumerable.Empty<string>().GetEnumerator();
    }

    public sealed class AlternateEnumerableQueue : Queue<int>, IEnumerable<string>
    {
        IEnumerator<string> IEnumerable<string>.GetEnumerator() => Enumerable.Empty<string>().GetEnumerator();
    }

    public sealed class AlternateEnumerableDictionary : Dictionary<string, int>, IEnumerable<string>
    {
        IEnumerator<string> IEnumerable<string>.GetEnumerator() => Enumerable.Empty<string>().GetEnumerator();
    }

    public interface IOptionsFactory
    {
        JsonSerializerOptions CreateEnumerableOptions();
        JsonSerializerOptions CreateDictionaryOptions();
    }

    public sealed class OptionsFactory<TSource> : IOptionsFactory
    {
        public JsonSerializerOptions CreateEnumerableOptions() => new JsonSerializerOptions()
            .AddJsonMigrationSupport(builder => builder.RegisterMigrator<BooleanEnumerableMigrator<TSource>>());

        public JsonSerializerOptions CreateDictionaryOptions() => new JsonSerializerOptions()
            .AddJsonMigrationSupport(builder => builder.RegisterMigrator<BooleanDictionaryMigrator<TSource>>());
    }

    public sealed class BooleanEnumerableMigrator<TSource> : IMigrate<List<bool>, EnumerableTarget<TSource>>
    {
        public bool TryMigrateFrom(List<bool> source, out EnumerableTarget<TSource> result)
            => throw new InvalidOperationException("Numeric input must not select Boolean migration.");
    }

    public sealed class BooleanDictionaryMigrator<TSource> : IMigrate<Dictionary<string, bool>, DictionaryTarget<TSource>>
    {
        public bool TryMigrateFrom(Dictionary<string, bool> source, out DictionaryTarget<TSource> result)
            => throw new InvalidOperationException("Numeric values must not select Boolean migration.");
    }
}
