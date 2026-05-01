namespace Egil.Orleans.Testing.Tests;

public class WaitForAssertionTimeoutExceptionTests
{
    [Fact]
    public void Constructor_preserves_message_and_inner_exception()
    {
        var innerException = new InvalidOperationException("boom");

        var exception = new WaitForAssertionTimeoutException("Timed out waiting for assertion.", innerException);

        Assert.Equal("Timed out waiting for assertion.", exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void Type_implements_timeout_marker_interface_for_xunit_v3()
    {
        Assert.Contains(
            typeof(WaitForAssertionTimeoutException).GetInterfaces(),
            static @interface => @interface.Name == "ITestTimeoutException");
    }
}
