using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages the top-down orthographic camera used during build mode.
///
/// Disables the regular gameplay camera and activates a dedicated
/// top-down orthographic camera. The player can pan with WASD and
/// zoom with the scroll wheel.
///
/// Wire up:
///   - mainFollowCamera: the Camera component that has the CinemachineBrain
///   - topDownCamera:    an orthographic Camera prefab (disabled by default)
///   Both are assigned via Inspector.
/// </summary>
public class BuildModeController : MonoBehaviour
{
    [Header("Cameras")]
    [Tooltip("The main gameplay Camera (the one with CinemachineBrain). Disabled during build mode.")]
    [SerializeField] private Camera mainFollowCamera;

    [Tooltip("Orthographic top-down Camera. Should be disabled in the prefab; enabled only in build mode.")]
    [SerializeField] private Camera topDownCamera;

    [Header("Top-Down Camera Height")]
    [SerializeField, Min(20f)]
    [Tooltip("Height in world units at which the top-down camera hovers.")]
    private float cameraHeight = 80f;

    [Header("Pan & Zoom")]
    [SerializeField, Min(1f)]  private float panSpeed = 22f;
    [SerializeField, Min(0.5f)] private float zoomSpeed = 5f;
    [SerializeField] private float minOrthoSize =  6f;
    [SerializeField] private float maxOrthoSize = 45f;

    public bool IsActive { get; private set; }

    // Saved fog state so we can restore it on exit.
    private bool _savedFog;
    private float _savedFogStart;
    private float _savedFogEnd;

    // ── Public API ────────────────────────────────────────────────────────────

    public void Enter(Vector3 worldFocus)
    {
        if (IsActive) return;
        IsActive = true;

        if (mainFollowCamera != null)
            mainFollowCamera.enabled = false;

        if (topDownCamera != null)
        {
            topDownCamera.enabled = true;
            topDownCamera.orthographic     = true;
            topDownCamera.orthographicSize = Mathf.Clamp(20f, minOrthoSize, maxOrthoSize);
            topDownCamera.transform.SetPositionAndRotation(
                new Vector3(worldFocus.x, cameraHeight, worldFocus.z),
                Quaternion.Euler(90f, 0f, 0f));
        }

        UnlockCursor();

        // Disable fog — at 80 units height everything looks solid blue through fog.
        _savedFog      = RenderSettings.fog;
        _savedFogStart = RenderSettings.fogStartDistance;
        _savedFogEnd   = RenderSettings.fogEndDistance;
        RenderSettings.fog = false;
    }

    public void Exit()
    {
        if (!IsActive) return;
        IsActive = false;

        if (topDownCamera    != null) topDownCamera.enabled    = false;
        if (mainFollowCamera != null) mainFollowCamera.enabled = true;

        LockCursor();

        // Restore fog state.
        RenderSettings.fog              = _savedFog;
        RenderSettings.fogStartDistance = _savedFogStart;
        RenderSettings.fogEndDistance   = _savedFogEnd;
    }

    // ── Camera returned for raycasts by BuildingSystem ────────────────────────
    public Camera TopDownCamera => topDownCamera;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Update()
    {
        if (!IsActive || topDownCamera == null) return;

        // Force cursor to stay unlocked — StarterAssets' OnApplicationFocus fires
        // even on disabled MonoBehaviours and can re-lock the cursor each frame.
        if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        HandlePan();
        HandleZoom();
    }

    // ── Input handlers ────────────────────────────────────────────────────────

    void HandlePan()
    {
        var kb = Keyboard.current;
        float h = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float v = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        if (h == 0f && v == 0f) return;

        var pos = topDownCamera.transform.position;
        pos.x += h * panSpeed * Time.deltaTime;
        pos.z += v * panSpeed * Time.deltaTime;
        topDownCamera.transform.position = pos;
    }

    void HandleZoom()
    {
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f) return;

        // Normalize to ±1 per scroll notch regardless of device scroll magnitude.
        // Each notch steps orthographic size by exactly zoomSpeed units.
        topDownCamera.orthographicSize = Mathf.Clamp(
            topDownCamera.orthographicSize - Mathf.Sign(scroll) * zoomSpeed,
            minOrthoSize, maxOrthoSize);
    }

    // ── Cursor helpers ────────────────────────────────────────────────────────

    static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }
}
