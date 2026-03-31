/// <summary>
/// Describes what the player is currently doing for metabolism drain rate purposes.
/// Inferred from intentional input (StarterAssetsInputs) so that external forces
/// like knockback or falling never inflate caloric/hydration drain rates.
/// </summary>
public enum CharacterActivity
{
    Resting,
    Walking,
    Running,
    Airborne,
    Wading
}
