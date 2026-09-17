namespace Egil.StronglyTypedPrimitives;

// ValidateAsync is only generated when the user's declaration names IAsyncValidatableObject
// (.NET 11), for the same reason as Validate: the ASP.NET Core validation source generator cannot
// see interfaces added by another generator. The interface extends IValidatableObject, so every
// case below also gets the generated Validate.
public class ValidateAsync_for_IAsyncValidatableObject_with_async_validation_attributes : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Required, RemoteCheck("https://example.com"), NotReserved] string Value) : IAsyncValidatableObject;

                public sealed class RemoteCheckAttribute(string url) : AsyncValidationAttribute
                {
                    public string Url { get; } = url;
                }

                public sealed class NotReservedAttribute : AsyncValidationAttribute
                {
                }
            }

            {{FakeAsyncValidationApi}}
            """);
}

public class ValidateAsync_for_IAsyncValidatableObject_without_async_validation_attributes : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Range(1, 10)] int Value) : IAsyncValidatableObject;
            }

            {{FakeAsyncValidationApi}}
            """);
}

public class ValidateAsync_for_IAsyncValidatableObject_on_alternative_parameter_name : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([RemoteCheck] int Data) : IAsyncValidatableObject;

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{FakeAsyncValidationApi}}
            """);
}

public class ValidateAsync_for_IAsyncValidatableObject_with_parameter_named_like_an_async_result_local : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Range(1, 10), RemoteCheck] int asyncResult0) : IAsyncValidatableObject;

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{FakeAsyncValidationApi}}
            """);
}

public class ValidateAsync_for_IAsyncValidatableObject_with_parameter_named_cancellationToken : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([RemoteCheck] int cancellationToken) : IAsyncValidatableObject;

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{FakeAsyncValidationApi}}
            """);
}

public class ValidateAsync_calls_a_user_written_explicit_Validate : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using System.Collections.Generic;
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([RemoteCheck] int Value) : IAsyncValidatableObject
                {
                    IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext)
                        => System.Array.Empty<ValidationResult>();
                }

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{FakeAsyncValidationApi}}
            """);
}

public class ValidateAsync_is_not_generated_when_the_user_writes_ValidateAsync : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using System.Collections.Generic;
            using System.Threading;
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([RemoteCheck] int Value) : IAsyncValidatableObject
                {
                    public async IAsyncEnumerable<ValidationResult> ValidateAsync(ValidationContext validationContext, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
                    {
                        await System.Threading.Tasks.Task.CompletedTask;
                        yield break;
                    }
                }

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{FakeAsyncValidationApi}}
            """);
}

public class ValidateAsync_is_not_generated_when_the_user_writes_an_explicit_ValidateAsync : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using System.Collections.Generic;
            using System.Threading;
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([RemoteCheck] int Value) : IAsyncValidatableObject
                {
                    async IAsyncEnumerable<ValidationResult> IAsyncValidatableObject.ValidateAsync(ValidationContext validationContext, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
                    {
                        await System.Threading.Tasks.Task.CompletedTask;
                        yield break;
                    }
                }

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{FakeAsyncValidationApi}}
            """);
}