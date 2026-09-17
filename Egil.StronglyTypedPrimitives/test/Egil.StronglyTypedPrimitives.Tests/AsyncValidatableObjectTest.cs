using Microsoft.CodeAnalysis.CSharp;

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

// A positional parameter called Validate is a property of that name, so the generated Validate is
// an explicit implementation and ValidateAsync has to reach it through the interface.
public class ValidateAsync_calls_the_generated_explicit_Validate : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Range(1, 10), RemoteCheck] int Validate) : IAsyncValidatableObject;

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{FakeAsyncValidationApi}}
            """);
}

// A positional parameter called ValidateAsync is a property of that name, which is not the
// interface member, so ValidateAsync is still generated, but a public one would collide with the
// property.
public class ValidateAsync_for_IAsyncValidatableObject_with_parameter_named_ValidateAsync : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([RemoteCheck] string ValidateAsync) : IAsyncValidatableObject;

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{FakeAsyncValidationApi}}
            """);
}

public class ValidateAsync_for_IAsyncValidatableObject_with_a_user_method_named_ValidateAsync : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using System.Threading.Tasks;
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([RemoteCheck] string Value) : IAsyncValidatableObject
                {
                    public Task<bool> ValidateAsync() => Task.FromResult(IsValueValid(Value, throwIfInvalid: false));
                }

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{FakeAsyncValidationApi}}
            """);
}

// The parameter is spelled with the @ escape because class is a keyword. ValidateAsync reads it
// as this.@class, escaped exactly once, on every language version the generator supports.
public class ValidateAsync_for_IAsyncValidatableObject_with_keyword_parameter_name : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Range(1, 10), RemoteCheck] int @class) : IAsyncValidatableObject;

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{FakeAsyncValidationApi}}
            """);
}

public class ValidateAsync_for_IAsyncValidatableObject_with_keyword_parameter_name_before_the_field_keyword : ValidationAttributeTestBase
{
    [Fact]
    public Task Test()
        => VerifyGeneratedSource($$"""
            using Egil.StronglyTypedPrimitives;
            using System.ComponentModel.DataAnnotations;

            namespace SomeNamespace
            {
                [StronglyTyped]
                public readonly partial record struct Foo([Range(1, 10), RemoteCheck] int @class) : IAsyncValidatableObject;

                public sealed class RemoteCheckAttribute : AsyncValidationAttribute
                {
                }
            }

            {{FakeAsyncValidationApi}}
            """, LanguageVersion.CSharp12);
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