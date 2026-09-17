#if NET10_0_OR_GREATER
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Egil.StronglyTypedPrimitives
{
    using Examples;

    // The ASP.NET Core validation source generator only sees the user's declarations, never the
    // partial this generator adds. Two consequences are pinned here: validation attributes on
    // the positional parameter are visible to it on the compiler-synthesized Value property, so it
    // validates those itself (keyed on Value, and IValidatableObject.Validate is not consulted once
    // a member has failed); a hand-written IsValueValid is invisible to it, so such a type is only
    // validated when the user declares IValidatableObject and the generated Validate runs.
    public class MinimalApiValidationTest
    {
        [Fact]
        public async Task Attribute_constrained_body_property_with_an_invalid_value_is_rejected()
        {
            await using var app = await StartApp();
            using var client = app.GetTestClient();

            using var response = await client.PostAsJsonAsync("/orders", new { quantity = 3 }, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ReadValidationProblem(response);
            var messages = Assert.Contains("Quantity.Value", problem.Errors);
            Assert.Equal([new RangeAttribute(6, 100).FormatErrorMessage("Value")], messages);
        }

        [Fact]
        public async Task Attribute_constrained_body_property_with_a_valid_value_is_accepted()
        {
            await using var app = await StartApp();
            using var client = app.GetTestClient();

            using var response = await client.PostAsJsonAsync("/orders", new { quantity = 10 }, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Attribute_constrained_route_parameter_with_an_invalid_value_is_rejected()
        {
            await using var app = await StartApp();
            using var client = app.GetTestClient();

            using var response = await client.GetAsync("/orders/3", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ReadValidationProblem(response);
            var messages = Assert.Contains("Value", problem.Errors);
            Assert.Equal([new RangeAttribute(6, 100).FormatErrorMessage("Value")], messages);
        }

        [Fact]
        public async Task Attribute_constrained_route_parameter_with_a_valid_value_is_accepted()
        {
            await using var app = await StartApp();
            using var client = app.GetTestClient();

            using var response = await client.GetAsync("/orders/10", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Attribute_constrained_type_without_a_declared_IValidatableObject_is_still_validated()
        {
            await using var app = await StartApp();
            using var client = app.GetTestClient();

            using var response = await client.PostAsJsonAsync("/unaware-orders", new { quantity = 3 }, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ReadValidationProblem(response);
            var messages = Assert.Contains("Quantity.Value", problem.Errors);
            Assert.Equal([new RangeAttribute(6, 100).FormatErrorMessage("Value")], messages);
        }

        [Fact]
        public async Task Hand_written_IsValueValid_with_a_declared_IValidatableObject_reports_its_message()
        {
            await using var app = await StartApp();
            using var client = app.GetTestClient();

            using var response = await client.PostAsJsonAsync("/constrained-orders", new { quantity = 3 }, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ReadValidationProblem(response);
            // The runtime calls Validate with no member name, and .NET 10 records a result without
            // member names under the empty key.
            var messages = Assert.Contains(string.Empty, problem.Errors);
            Assert.Equal([new ArgumentException("Value must be larger than 5", "value").Message], messages);
        }

        [Fact]
        public async Task Hand_written_IsValueValid_route_parameter_with_an_invalid_value_is_rejected()
        {
            await using var app = await StartApp();
            using var client = app.GetTestClient();

            using var response = await client.GetAsync("/constrained-orders/3", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ReadValidationProblem(response);
            var messages = Assert.Contains(string.Empty, problem.Errors);
            Assert.Equal([new ArgumentException("Value must be larger than 5", "value").Message], messages);
        }

        // Documented limitation: without the declared interface nothing tells the validation
        // generator about a hand-written IsValueValid, so the invalid payload (deserialized to
        // Empty) passes straight through.
        [Fact]
        public async Task Hand_written_IsValueValid_without_a_declared_IValidatableObject_is_not_validated()
        {
            await using var app = await StartApp();
            using var client = app.GetTestClient();

            using var response = await client.PostAsJsonAsync("/unaware-constrained-orders", new { quantity = 3 }, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private static async Task<HttpValidationProblemDetails> ReadValidationProblem(HttpResponseMessage response)
        {
            var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(TestContext.Current.CancellationToken);
            Assert.NotNull(problem);
            return problem;
        }

        private static async Task<WebApplication> StartApp()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
#pragma warning disable ASP0029 // AddValidation is experimental in .NET 10.
            builder.Services.AddValidation();
#pragma warning restore ASP0029

            var app = builder.Build();
            app.MapPost("/orders", (OrderDto dto) => Results.Ok());
            app.MapGet("/orders/{quantity}", (StronglyTypedQuantityWithValidate quantity) => Results.Ok());
            app.MapPost("/unaware-orders", (UnawareOrderDto dto) => Results.Ok());
            app.MapPost("/constrained-orders", (ConstrainedOrderDto dto) => Results.Ok());
            app.MapGet("/constrained-orders/{quantity}", (StronglyTypedIntWithConstraintsAndValidate quantity) => Results.Ok());
            app.MapPost("/unaware-constrained-orders", (UnawareConstrainedOrderDto dto) => Results.Ok());
            await app.StartAsync(TestContext.Current.CancellationToken);
            return app;
        }

        public sealed class OrderDto
        {
            public StronglyTypedQuantityWithValidate Quantity { get; set; }
        }

        public sealed class UnawareOrderDto
        {
            public StronglyTypedIntWithRange Quantity { get; set; }
        }

        public sealed class ConstrainedOrderDto
        {
            public StronglyTypedIntWithConstraintsAndValidate Quantity { get; set; }
        }

        public sealed class UnawareConstrainedOrderDto
        {
            public StronglyTypedIntWithConstraints Quantity { get; set; }
        }
    }
}
#endif