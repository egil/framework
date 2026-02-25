using System.Linq.Expressions;
using System.Reflection;
using Orleans.Serialization;

namespace Egil.Orleans.StateMigration.SystemTextJson;

internal static class DeserializationCallbackInvoker
{
    private static readonly IServiceProvider EmptyProvider = new EmptyServiceProvider();
    private static readonly DeserializationContext EmptyContext =
        new StateMigrationDeserializationContext(EmptyProvider, EmptyProvider);

    public static Func<TState, TState> CreateInvoker<TState>(IServiceProvider? serviceProvider)
    {
        if (!typeof(IOnDeserialized).IsAssignableFrom(typeof(TState)))
        {
            return static state => state;
        }

        Action<IOnDeserialized> callbackInvoker = CreateCallbackInvoker(serviceProvider);
        return state =>
        {
            callbackInvoker((IOnDeserialized)(object)state!);
            return state;
        };
    }

    private static Action<IOnDeserialized> CreateCallbackInvoker(IServiceProvider? serviceProvider)
    {
        if (serviceProvider is not null
            && OrleansOnDeserializedCallbackInvoker.TryCreate(serviceProvider, out Action<IOnDeserialized>? invoke))
        {
            return invoke!;
        }

        DeserializationContext context = CreateFallbackContext(serviceProvider);
        return callback => callback.OnDeserialized(context);
    }

    private static DeserializationContext CreateFallbackContext(IServiceProvider? serviceProvider)
    {
        if (serviceProvider is null)
        {
            return EmptyContext;
        }

        object runtimeClient = OrleansOnDeserializedCallbackInvoker.TryResolveRuntimeClient(serviceProvider) ?? serviceProvider;
        return new StateMigrationDeserializationContext(serviceProvider, runtimeClient);
    }

    private static class OrleansOnDeserializedCallbackInvoker
    {
        private const string OnDeserializedCallbacksTypeName = "Orleans.Runtime.OnDeserializedCallbacks, Orleans.Core";
        private const string OnDeserializedCallbacksNamespaceName = "Orleans.Runtime.OnDeserializedCallbacks";
        private const string OrleansCoreAssemblyName = "Orleans.Core";
        private const string RuntimeClientTypeName = "Orleans.Runtime.IRuntimeClient, Orleans.Core";
        private static readonly Type? CallbackType = ResolveCallbackType();
        private static readonly Type? RuntimeClientType = ResolveRuntimeClientType();
        private static readonly ConstructorInfo? CallbackConstructor = CallbackType?.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(IServiceProvider)],
            modifiers: null);
        private static readonly Action<object, IOnDeserialized>? InvokeCallback =
            CreateInvokeCallbackDelegate();

        public static bool TryCreate(IServiceProvider serviceProvider, out Action<IOnDeserialized>? invoke)
        {
            invoke = null;
            if (CallbackType is null || InvokeCallback is null)
            {
                return false;
            }

            object? callback = TryResolveCallback(serviceProvider) ?? TryCreateCallback(serviceProvider);
            if (callback is null)
            {
                return false;
            }

            invoke = value => InvokeCallback(callback, value);
            return true;
        }

        private static object? TryResolveCallback(IServiceProvider serviceProvider)
            => serviceProvider.GetService(CallbackType!);

        public static object? TryResolveRuntimeClient(IServiceProvider serviceProvider)
        {
            if (RuntimeClientType is null)
            {
                return null;
            }

            return serviceProvider.GetService(RuntimeClientType);
        }

        private static object? TryCreateCallback(IServiceProvider serviceProvider)
        {
            if (CallbackConstructor is null)
            {
                return null;
            }

            try
            {
                return CallbackConstructor.Invoke([serviceProvider]);
            }
            catch (TargetInvocationException)
            {
                return null;
            }
            catch (MemberAccessException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static Action<object, IOnDeserialized>? CreateInvokeCallbackDelegate()
        {
            if (CallbackType is null)
            {
                return null;
            }

            MethodInfo? onDeserialized = CallbackType.GetMethod(
                nameof(IOnDeserialized.OnDeserialized),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(IOnDeserialized)],
                modifiers: null);

            if (onDeserialized is null)
            {
                return null;
            }

            ParameterExpression callbackInstance = Expression.Parameter(typeof(object), "callbackInstance");
            ParameterExpression value = Expression.Parameter(typeof(IOnDeserialized), "value");
            MethodCallExpression invoke = Expression.Call(
                Expression.Convert(callbackInstance, CallbackType),
                onDeserialized,
                value);
            return Expression.Lambda<Action<object, IOnDeserialized>>(invoke, callbackInstance, value).Compile();
        }

        private static Type? ResolveCallbackType()
        {
            return Type.GetType(OnDeserializedCallbacksTypeName, throwOnError: false)
                   ?? ResolveOrleansCoreType(OnDeserializedCallbacksNamespaceName);
        }

        private static Type? ResolveRuntimeClientType()
        {
            return Type.GetType(RuntimeClientTypeName, throwOnError: false)
                   ?? ResolveOrleansCoreType("Orleans.Runtime.IRuntimeClient");
        }

        private static Type? ResolveOrleansCoreType(string fullTypeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, OrleansCoreAssemblyName, StringComparison.Ordinal))
                {
                    return assembly.GetType(fullTypeName, throwOnError: false);
                }
            }

            try
            {
                return Assembly.Load(OrleansCoreAssemblyName).GetType(fullTypeName, throwOnError: false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (FileLoadException)
            {
                return null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }
    }

    private sealed class StateMigrationDeserializationContext(
        IServiceProvider serviceProvider,
        object runtimeClient) : DeserializationContext
    {
        public override IServiceProvider ServiceProvider { get; } = serviceProvider;

        public override object RuntimeClient { get; } = runtimeClient;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
