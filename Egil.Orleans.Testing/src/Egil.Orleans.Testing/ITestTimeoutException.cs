namespace Egil.Orleans.Testing;

/// <summary>
/// Marks an exception as a test-timeout failure for frameworks that support marker interfaces.
/// </summary>
/// <remarks>
/// xUnit.net v3 recognizes an interface named <c>ITestTimeoutException</c> in any namespace
/// and uses it to classify failures as timeouts without requiring a hard dependency on xUnit.
/// Other test frameworks may ignore this marker.
/// </remarks>
public interface ITestTimeoutException
{
}
