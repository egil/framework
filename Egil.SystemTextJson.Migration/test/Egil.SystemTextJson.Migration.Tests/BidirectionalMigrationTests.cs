using System.Text.Json;

namespace Egil.SystemTextJson.Migration.Tests;

public class BidirectionalMigrationTests
{
    private readonly JsonSerializerOptions options;

    public BidirectionalMigrationTests()
    {
        options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.AddJsonMigrationSupport();
    }

    [Fact]
    public void Static_bidirectional_migrate_X_to_Y()
    {
        var x = new BlueGreenX("hello", 42);
        var json = JsonSerializer.Serialize(x, options);

        var y = JsonSerializer.Deserialize<BlueGreenY>(json, options);

        Assert.NotNull(y);
        Assert.Equal("hello", y.Label);
        Assert.Equal(42, y.Value);
    }

    [Fact]
    public void Static_bidirectional_migrate_Y_to_X()
    {
        var y = new BlueGreenY("world", 99);
        var json = JsonSerializer.Serialize(y, options);

        var x = JsonSerializer.Deserialize<BlueGreenX>(json, options);

        Assert.NotNull(x);
        Assert.Equal("world", x.Name);
        Assert.Equal(99, x.Age);
    }

    [Fact]
    public void Static_bidirectional_roundtrip_X_no_migration()
    {
        var x = new BlueGreenX("test", 1);
        var json = JsonSerializer.Serialize(x, options);

        var result = JsonSerializer.Deserialize<BlueGreenX>(json, options);

        Assert.Equal(x, result);
    }

    [Fact]
    public void Static_bidirectional_roundtrip_Y_no_migration()
    {
        var y = new BlueGreenY("test", 1);
        var json = JsonSerializer.Serialize(y, options);

        var result = JsonSerializer.Deserialize<BlueGreenY>(json, options);

        Assert.Equal(y, result);
    }

    [Fact]
    public void External_bidirectional_migrate_X_to_Y()
    {
        var extOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        extOptions.AddJsonMigrationSupport(builder =>
        {
            builder.RegisterMigrator<BlueGreenExternalXtoY>();
            builder.RegisterMigrator<BlueGreenExternalYtoX>();
        });

        var x = new BlueGreenExtX("hello", 42);
        var json = JsonSerializer.Serialize(x, extOptions);

        var y = JsonSerializer.Deserialize<BlueGreenExtY>(json, extOptions);

        Assert.NotNull(y);
        Assert.Equal("hello", y.Label);
        Assert.Equal(42, y.Value);
    }

    [Fact]
    public void External_bidirectional_migrate_Y_to_X()
    {
        var extOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        extOptions.AddJsonMigrationSupport(builder =>
        {
            builder.RegisterMigrator<BlueGreenExternalXtoY>();
            builder.RegisterMigrator<BlueGreenExternalYtoX>();
        });

        var y = new BlueGreenExtY("world", 99);
        var json = JsonSerializer.Serialize(y, extOptions);

        var x = JsonSerializer.Deserialize<BlueGreenExtX>(json, extOptions);

        Assert.NotNull(x);
        Assert.Equal("world", x.Name);
        Assert.Equal(99, x.Age);
    }
}

// Static bidirectional migration types
[JsonMigratable(TypeDiscriminator = "blue-green-x")]
public record class BlueGreenX(string Name, int Age) :
    IMigrateFrom<BlueGreenY, BlueGreenX>
{
    public static bool TryMigrateFrom(BlueGreenY source, out BlueGreenX result)
    {
        result = new BlueGreenX(source.Label, source.Value);
        return true;
    }
}

[JsonMigratable(TypeDiscriminator = "blue-green-y")]
public record class BlueGreenY(string Label, int Value) :
    IMigrateFrom<BlueGreenX, BlueGreenY>
{
    public static bool TryMigrateFrom(BlueGreenX source, out BlueGreenY result)
    {
        result = new BlueGreenY(source.Name, source.Age);
        return true;
    }
}

// External bidirectional migration types
[JsonMigratable(TypeDiscriminator = "blue-green-ext-x")]
public record class BlueGreenExtX(string Name, int Age);

[JsonMigratable(TypeDiscriminator = "blue-green-ext-y")]
public record class BlueGreenExtY(string Label, int Value);

public class BlueGreenExternalXtoY : IMigrate<BlueGreenExtX, BlueGreenExtY>
{
    public bool TryMigrateFrom(BlueGreenExtX source, out BlueGreenExtY result)
    {
        result = new BlueGreenExtY(source.Name, source.Age);
        return true;
    }
}

public class BlueGreenExternalYtoX : IMigrate<BlueGreenExtY, BlueGreenExtX>
{
    public bool TryMigrateFrom(BlueGreenExtY source, out BlueGreenExtX result)
    {
        result = new BlueGreenExtX(source.Label, source.Value);
        return true;
    }
}
