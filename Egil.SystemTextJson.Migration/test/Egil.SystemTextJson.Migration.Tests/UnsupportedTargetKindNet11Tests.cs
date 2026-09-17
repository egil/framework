#if NET11_0_OR_GREATER
using System.Text.Json;

namespace Egil.SystemTextJson.Migration.Tests;

public partial class UnsupportedTargetKindTests
{
    [Fact]
    public void Migratable_union_target_throws_not_supported_with_guidance()
    {
        var options = CreateOptions();

        var exception = Assert.ThrowsAny<NotSupportedException>(() => JsonSerializer.Deserialize<IntOrStringUnion>("42", options));

        Assert.Contains(typeof(IntOrStringUnion).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Union", exception.Message, StringComparison.Ordinal);
        Assert.Contains("JSON object", exception.Message, StringComparison.Ordinal);
    }

    [JsonMigratable]
    public union IntOrStringUnion(int, string);
}
#endif
