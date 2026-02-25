using System.Reflection;
using System.Reflection.Emit;

namespace Egil.Orleans.StateMigration.Tests;

public sealed class StateTypeIdentityTests
{
    private static readonly Type IdentityType =
        typeof(Storage<>).Assembly.GetType("Egil.Orleans.StateMigration.StateTypeIdentity", throwOnError: true)!;

    private static readonly MethodInfo GetIdentityMethod =
        IdentityType.GetMethod("GetIdentity", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find StateTypeIdentity.GetIdentity.");

    private static readonly MethodInfo RegisterRangeMethod =
        IdentityType.GetMethod("RegisterRange", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find StateTypeIdentity.RegisterRange.");

    private static readonly MethodInfo TryResolveMethod =
        IdentityType.GetMethod("TryResolve", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find StateTypeIdentity.TryResolve.");

    [Fact]
    public void Get_identity_prefers_orleans_alias()
    {
        string identity = GetIdentity(typeof(StateTypeIdentityAliasedState));

        Assert.Equal("state-type-identity/aliased", identity);
    }

    [Fact]
    public void Get_identity_falls_back_to_full_type_name_without_alias()
    {
        string identity = GetIdentity(typeof(StateTypeIdentityNonAliasedState));

        Assert.Equal(typeof(StateTypeIdentityNonAliasedState).FullName, identity);
    }

    [Fact]
    public void Try_resolve_finds_type_by_alias_through_scan()
    {
        bool resolved = TryResolve("state-type-identity/aliased", out Type? type);

        Assert.True(resolved);
        Assert.Equal(typeof(StateTypeIdentityAliasedState), type);
    }

    [Fact]
    public void Try_resolve_negative_cache_is_cleared_when_type_is_registered()
    {
        string identity = $"StateTypeIdentity.DynamicPromoted.{Guid.NewGuid():N}";
        bool initiallyResolved = TryResolve(identity, out _);
        Assert.False(initiallyResolved);

        Type promoted = CreateRuntimeType(
            assemblyName: $"StateTypeIdentity.Dynamic.{Guid.NewGuid():N}",
            typeName: identity);
        RegisterRange([promoted]);

        bool resolvedAfterRegistration = TryResolve(identity, out Type? resolvedType);
        Assert.True(resolvedAfterRegistration);
        Assert.Equal(promoted, resolvedType);
    }

    [Fact]
    public void Register_range_throws_for_duplicate_identities()
    {
        Type first = CreateRuntimeType(
            assemblyName: $"StateTypeIdentity.Dynamic.{Guid.NewGuid():N}",
            typeName: "StateTypeIdentity.DynamicDuplicate");
        Type second = CreateRuntimeType(
            assemblyName: $"StateTypeIdentity.Dynamic.{Guid.NewGuid():N}",
            typeName: "StateTypeIdentity.DynamicDuplicate");

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => RegisterRange([first, second]));

        InvalidOperationException inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("Multiple types resolve to the same state identity", inner.Message, StringComparison.Ordinal);
    }

    private static string GetIdentity(Type type)
        => (string)GetIdentityMethod.Invoke(null, [type])!;

    private static void RegisterRange(Type[] types)
        => _ = RegisterRangeMethod.Invoke(null, [types]);

    private static bool TryResolve(string identity, out Type? resolvedType)
    {
        object?[] arguments = [identity, null];
        bool resolved = (bool)TryResolveMethod.Invoke(null, arguments)!;
        resolvedType = (Type?)arguments[1];
        return resolved;
    }

    private static Type CreateRuntimeType(string assemblyName, string typeName)
    {
        AssemblyName name = new(assemblyName);
        AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule($"{assemblyName}.dll");
        TypeBuilder typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class);
        return typeBuilder.CreateType()!;
    }
}

[Alias("state-type-identity/aliased")]
public sealed class StateTypeIdentityAliasedState;

public sealed class StateTypeIdentityNonAliasedState;
