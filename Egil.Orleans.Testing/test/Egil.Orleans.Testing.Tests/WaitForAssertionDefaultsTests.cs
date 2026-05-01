using System.Globalization;
using System.Reflection;

namespace Egil.Orleans.Testing.Tests;

public class WaitForAssertionDefaultsTests
{
    private const string TimeoutVariableName = "WAIT_FOR_ASSERTION_TIMEOUT_SECONDS";

    [Fact]
    public void LoadTimeout_returns_default_when_variable_is_missing()
    {
        var timeout = WithEnvironmentVariable(TimeoutVariableName, null, InvokeLoadTimeout);

        Assert.Equal(TimeSpan.FromSeconds(5), timeout);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void LoadTimeout_returns_default_when_variable_is_invalid(string value)
    {
        var timeout = WithEnvironmentVariable(TimeoutVariableName, value, InvokeLoadTimeout);

        Assert.Equal(TimeSpan.FromSeconds(5), timeout);
    }

    [Fact]
    public void LoadTimeout_returns_configured_positive_timeout()
    {
        var timeout = WithEnvironmentVariable(TimeoutVariableName, 2.5.ToString(CultureInfo.InvariantCulture), InvokeLoadTimeout);

        Assert.Equal(TimeSpan.FromSeconds(2.5), timeout);
    }

    private static TimeSpan InvokeLoadTimeout()
        => (TimeSpan)typeof(WaitForAssertionDefaults)
            .GetMethod("LoadTimeout", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(obj: null, parameters: null)!;

    private static T WithEnvironmentVariable<T>(string variableName, string? value, Func<T> callback)
    {
        var previous = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, value);
            return callback();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
        }
    }
}
