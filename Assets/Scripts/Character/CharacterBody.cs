using UnityEngine;

[CreateAssetMenu(menuName = "Biomolecular/CharacterBody")]
public class CharacterBody : ScriptableObject
{
    [Header("Locomotion")]
    public float walkSpeed = 2.5f;
    public float runSpeed = 5.5f;
    public float jumpHeight = 1.2f;
    public float gravity = -15f;
    public float rotationSmoothTime = 0.12f;
    public float speedChangeRate = 10f;
    public float groundCheckRadius = 0.28f;
    public float groundCheckOffset = -0.14f;
    public float jumpTimeout = 0.50f;
    public float fallTimeout = 0.15f;
    public float wadingSpeedMultiplier = 0.5f;

    [Header("Mass & Carrying")]
    public float bodyMassKg = 70f;
    public float maxCarryMassKg = 30f;

    [Header("Metabolism — Starting State")]
    public float maxCaloricReserveKcal = 2000f;
    public float maxHydrationReserveGrams = 2000f;
    [Range(0f, 1f)] public float startingCaloricFraction = 0.75f;
    [Range(0f, 1f)] public float startingHydrationFraction = 0.75f;
}
