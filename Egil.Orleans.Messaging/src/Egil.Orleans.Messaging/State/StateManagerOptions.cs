using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Egil.Orleans.Messaging.State;

/// <summary>Configures recovery after a persistent-state mutation fails.</summary>
/// <remarks>
/// Defaults are overridden by silo-wide configuration, named factory configuration,
/// then grain-local configuration. Each manager receives an isolated lifetime snapshot.
/// </remarks>
public sealed class StateManagerOptions
{
    /// <summary>Gets or sets the recovery policy. The default ends the failed activation.</summary>
    public StateRecoveryPolicy RecoveryPolicy { get; set; } = StateRecoveryPolicy.FenceAndDeactivate;

    internal static StateManagerOptions Resolve(IServiceProvider services, string? storageName, Action<StateManagerOptions>? configure)
    {
        // IOptionsFactory creates a fresh instance, so callbacks cannot change another
        // manager's snapshot through a cached named options object.
        var options = services.GetRequiredService<IOptionsFactory<StateManagerOptions>>()
            .Create(storageName ?? Options.DefaultName);
        configure?.Invoke(options);
        var snapshot = new StateManagerOptions { RecoveryPolicy = options.RecoveryPolicy };
        if (!Enum.IsDefined(snapshot.RecoveryPolicy))
        {
            throw new OptionsValidationException(storageName ?? Options.DefaultName, typeof(StateManagerOptions),
                ["RecoveryPolicy must be a defined StateRecoveryPolicy value."]);
        }

        return snapshot;
    }
}
