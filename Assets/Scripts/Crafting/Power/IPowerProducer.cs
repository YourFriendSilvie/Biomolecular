/// <summary>
/// Implemented by anything that generates electricity for the PowerGrid.
/// e.g. SteamGenerator, future solar panel, chemical fuel cell.
/// </summary>
public interface IPowerProducer
{
    /// <summary>Instantaneous output in Watts this frame.</summary>
    float CurrentOutputWatts { get; }
}
