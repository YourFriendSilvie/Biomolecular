/// <summary>
/// Implemented by any machine that draws electricity from the PowerGrid.
/// The PowerGrid sets IsPowered each frame based on available supply.
/// </summary>
public interface IPowerConsumer
{
    /// <summary>Watts this machine needs while active.</summary>
    float RequiredWatts { get; }

    /// <summary>Set by PowerGrid each frame: true if this machine has power.</summary>
    bool IsPowered { get; set; }

    /// <summary>Lower value = higher priority when supply is limited.</summary>
    int PowerPriority { get; }

    /// <summary>True when the machine is currently trying to operate (not idle/off).</summary>
    bool IsActive { get; }
}
