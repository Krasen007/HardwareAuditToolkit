namespace HardwareAuditToolkit.App.Services;

/// <summary>
/// The single routing table from module id → screen view-model factory (roadmap E1).
/// Built once in the DI composition root. The dashboard cards are generated from the
/// same modules' <c>IModuleMetadata</c>, so adding a module means registering one
/// entry here (plus its DI/VM/DataTemplate entries) instead of editing a hardcoded
/// dashboard list and a navigation switch.
/// </summary>
public sealed class ModuleScreenRegistry
{
    private readonly Dictionary<string, Func<IServiceProvider, object>> _factories;

    public ModuleScreenRegistry(IEnumerable<KeyValuePair<string, Func<IServiceProvider, object>>> factories)
        => _factories = new(factories, StringComparer.OrdinalIgnoreCase);

    public bool Contains(string moduleId)
        => _factories.ContainsKey(moduleId);

    public object Resolve(string moduleId, IServiceProvider services)
    {
        if (!_factories.TryGetValue(moduleId, out var factory))
        {
            throw new ArgumentException($"Unknown module id '{moduleId}'.", nameof(moduleId));
        }

        return factory(services);
    }
}
