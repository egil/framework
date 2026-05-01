namespace Egil.Orleans.Testing.Tests;

public class RequestContextScopeTests
{
    [Fact]
    public void ForAssertion_sets_assertion_key_and_restores_previous_value()
    {
        RequestContext.Set(RequestContextScope.AssertionKey, "previous");

        using (RequestContextScope.ForAssertion())
        {
            Assert.Equal(true, RequestContext.Get(RequestContextScope.AssertionKey));
        }

        Assert.Equal("previous", RequestContext.Get(RequestContextScope.AssertionKey));
        RequestContext.Remove(RequestContextScope.AssertionKey);
    }

    [Fact]
    public void ForAssertion_removes_key_when_it_was_not_previously_set()
    {
        RequestContext.Remove(RequestContextScope.AssertionKey);

        using (RequestContextScope.ForAssertion())
        {
            Assert.Equal(true, RequestContext.Get(RequestContextScope.AssertionKey));
        }

        Assert.Null(RequestContext.Get(RequestContextScope.AssertionKey));
    }
}
