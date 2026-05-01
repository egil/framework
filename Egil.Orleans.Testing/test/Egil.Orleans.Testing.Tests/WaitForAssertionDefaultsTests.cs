namespace Egil.Orleans.Testing.Tests;

public class IGrainActivityWaiterDefaultsTests
{
    private const string TimeoutVariableName = "WAIT_FOR_ASSERTION_TIMEOUT_SECONDS";

    [Fact]
    public void LoadDefaultWaitTimeout_returns_default_when_variable_is_missing()
    {
        var timeout = WithEnvironmentVariable(TimeoutVariableName, null, IGrainActivityWaiter.LoadDefaultWaitTimeout);

        Assert.Equal(TimeSpan.FromSeconds(5), timeout);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void LoadDefaultWaitTimeout_returns_default_when_variable_is_invalid(string value)
    {
        var timeout = WithEnvironmentVariable(TimeoutVariableName, value, IGrainActivityWaiter.LoadDefaultWaitTimeout);

        Assert.Equal(TimeSpan.FromSeconds(5), timeout);
    }

    [Fact]
    public void LoadDefaultWaitTimeout_returns_configured_positive_timeout()
    {
        var timeout = WithEnvironmentVariable(TimeoutVariableName, "2.5", IGrainActivityWaiter.LoadDefaultWaitTimeout);

        Assert.Equal(TimeSpan.FromSeconds(2.5), timeout);
    }

    [Fact]
    public void DefaultWaitTimeout_can_be_overridden_in_code()
    {
        var previous = IGrainActivityWaiter.DefaultWaitTimeout;

        try
        {
            IGrainActivityWaiter.DefaultWaitTimeout = TimeSpan.FromSeconds(9);

            Assert.Equal(TimeSpan.FromSeconds(9), IGrainActivityWaiter.DefaultWaitTimeout);
        }
        finally
        {
            IGrainActivityWaiter.DefaultWaitTimeout = previous;
        }
    }

    [Fact]
    public void DefaultWaitTimeout_rejects_non_positive_values()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            IGrainActivityWaiter.DefaultWaitTimeout = TimeSpan.Zero);

        Assert.Equal("value", exception.ParamName);
    }

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
