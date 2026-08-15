using UnityEngine;

/// <summary>
/// Camera controls used only by the independent Dreamscape art audition scene.
/// </summary>
[DisallowMultipleComponent]
public sealed class DreamscapeArtAuditionCameraController : MonoBehaviour
{
    private const float NearDistance = 6.5f;
    private const float MidDistance = 10.5f;
    private const float FarDistance = 15.5f;

    [SerializeField] private Vector3 pivot = new Vector3(0f, 0.65f, 0f);
    [SerializeField] private float pitch = 40f;
    [SerializeField] private float yaw = 12f;
    [SerializeField] private float distance = MidDistance;
    [SerializeField] private float scrollSensitivity = 1.25f;

    private string CurrentViewName
    {
        get
        {
            if (Mathf.Abs(distance - NearDistance) < 0.01f) return "Near";
            if (Mathf.Abs(distance - MidDistance) < 0.01f) return "Mid";
            if (Mathf.Abs(distance - FarDistance) < 0.01f) return "Far";
            return "Custom";
        }
    }

    public void Configure(Vector3 targetPivot, float targetPitch, float targetYaw, float targetDistance)
    {
        pivot = targetPivot;
        pitch = targetPitch;
        yaw = targetYaw;
        distance = targetDistance;
        ApplyCameraTransform();
    }

    public void ApplyNearView()
    {
        distance = NearDistance;
        ApplyCameraTransform();
    }

    public void ApplyMidView()
    {
        distance = MidDistance;
        ApplyCameraTransform();
    }

    public void ApplyFarView()
    {
        distance = FarDistance;
        ApplyCameraTransform();
    }

    private void OnEnable()
    {
        ApplyCameraTransform();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) ApplyNearView();
        if (Input.GetKeyDown(KeyCode.Alpha2)) ApplyMidView();
        if (Input.GetKeyDown(KeyCode.Alpha3)) ApplyFarView();

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.001f)
        {
            distance = Mathf.Clamp(distance - scroll * scrollSensitivity, NearDistance, FarDistance);
            ApplyCameraTransform();
        }
    }

    private void ApplyCameraTransform()
    {
        float pitchRadians = pitch * Mathf.Deg2Rad;
        Vector3 baseOffset = new Vector3(0f, Mathf.Sin(pitchRadians), -Mathf.Cos(pitchRadians)) * distance;
        Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * baseOffset;
        transform.position = pivot + offset;
        transform.rotation = Quaternion.LookRotation(pivot - transform.position);
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(16f, 16f, 354f, 116f), "Art Audition Camera");
        if (GUI.Button(new Rect(30f, 44f, 96f, 30f), "Near (1)")) ApplyNearView();
        if (GUI.Button(new Rect(136f, 44f, 96f, 30f), "Mid (2)")) ApplyMidView();
        if (GUI.Button(new Rect(242f, 44f, 96f, 30f), "Far (3)")) ApplyFarView();
        GUI.Label(new Rect(30f, 80f, 308f, 20f),
            "Current: " + CurrentViewName + "    Distance: " + distance.ToString("0.0"));
        GUI.Label(new Rect(30f, 101f, 308f, 20f), "Mouse wheel: continuous zoom");
    }
}
