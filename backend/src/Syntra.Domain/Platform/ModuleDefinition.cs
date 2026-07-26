namespace Syntra.Domain.Platform;

// Named ModuleDefinition, not Module, to avoid clashing with System.Reflection.Module.
public class ModuleDefinition
{
    public int ModuleId { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public ModuleCategory Category { get; set; }

    public ICollection<TenantModule> TenantModules { get; set; } = new List<TenantModule>();
}
