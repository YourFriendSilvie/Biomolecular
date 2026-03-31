using UnityEngine;

/// <summary>
/// Ghost preview rendered while the player is choosing where to place a building.
///
/// Tints all child Renderers green (valid placement) or red (blocked) via
/// MaterialPropertyBlock so the original materials on the prefab are never modified.
///
/// Added automatically by BuildingSystem when a building is selected;
/// all MonoBehaviour components other than BuildingGhost are disabled on the clone
/// so machines / colliders don't fire during preview.
/// </summary>
[DisallowMultipleComponent]
public class BuildingGhost : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    static readonly Color ValidColor   = new(0.15f, 1f,   0.25f, 0.40f);
    static readonly Color InvalidColor = new(1f,   0.15f, 0.10f, 0.40f);

    Renderer[]          _renderers;
    MaterialPropertyBlock _mpb;
    bool                _valid = true;

    public bool IsValid => _valid;

    void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        _mpb = new MaterialPropertyBlock();
        ApplyColor(ValidColor);
    }

    /// <summary>Set green/red tint based on whether the current position is valid.</summary>
    public void SetValid(bool valid)
    {
        if (valid == _valid) return;
        _valid = valid;
        ApplyColor(valid ? ValidColor : InvalidColor);
    }

    /// <summary>Move and rotate the ghost to a world position.</summary>
    public void UpdatePose(Vector3 position, Quaternion rotation)
        => transform.SetPositionAndRotation(position, rotation);

    void ApplyColor(Color color)
    {
        _mpb.SetColor(BaseColorId, color);
        foreach (var r in _renderers)
            r.SetPropertyBlock(_mpb);
    }
}
