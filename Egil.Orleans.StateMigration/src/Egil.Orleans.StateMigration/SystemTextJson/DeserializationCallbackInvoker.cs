using Orleans.Serialization;

namespace Egil.Orleans.StateMigration.SystemTextJson;

internal static class DeserializationCallbackInvoker
{
    private static readonly DeserializationContext Context = new StateMigrationDeserializationContext();

    public static TState Invoke<TState>(TState state)
    {
        if (state is IOnDeserialized callback)
        {
            callback.OnDeserialized(Context);
        }

        return state;
    }

    private sealed class StateMigrationDeserializationContext : DeserializationContext
    {
        private static readonly IServiceProvider EmptyProvider = new EmptyServiceProvider();

        public override IServiceProvider ServiceProvider => EmptyProvider;

        public override object RuntimeClient => EmptyProvider;

        private sealed class EmptyServiceProvider : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }
    }
}
