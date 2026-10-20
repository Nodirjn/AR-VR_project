using UnityEngine;
using UnityEngine.XR;

/// <summary>Places the active desktop camera or XR Origin at the shared exterior start point.</summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed class ExteriorSpawnPlacement : MonoBehaviour
{
    [SerializeField] private Transform spawnPoint;
    [SerializeField, Min(0.5f)] private float desktopEyeHeight = 1.7f;

    private void Awake()
    {
        if (spawnPoint == null)
        {
            Transform child = transform.Find("Player Spawn");
            if (child != null) spawnPoint = child;
        }
        if (spawnPoint == null) return;

        Camera[] cameras = Camera.allCameras;
        Camera chosen = null;
        Transform xrRoot = null;
        bool xrActive = XRSettings.isDeviceActive;
        if (xrActive)
        {
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera candidate = cameras[i];
                if (candidate == null || !candidate.isActiveAndEnabled) continue;
                Transform root = FindXrRoot(candidate.transform);
                if (root == null) continue;
                chosen = candidate;
                xrRoot = root;
                break;
            }
            if (xrRoot == null)
            {
                Transform[] loadedTransforms = Resources.FindObjectsOfTypeAll<Transform>();
                for (int i = 0; i < loadedTransforms.Length; i++)
                {
                    Transform candidate = loadedTransforms[i];
                    if (candidate == null || !candidate.gameObject.scene.IsValid() || candidate.parent != null) continue;
                    if (candidate.name.IndexOf("XR Origin", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                        candidate.name.IndexOf("XR Rig", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    xrRoot = candidate;
                    chosen = candidate.GetComponentInChildren<Camera>(true);
                    break;
                }
            }
            if (xrRoot == null)
            {
                Debug.LogWarning("Exterior spawn was not applied: the active XR camera's rig root could not be resolved.", this);
                return;
            }
        }
        if (chosen == null) chosen = Camera.main;
        if (chosen == null) return;

        // The authored scene camera clears to a solid beige color. Use the scene's URP-compatible
        // skybox on whichever desktop or XR camera owns the exterior spawn.
        chosen.clearFlags = CameraClearFlags.Skybox;
        Quaternion heading = Quaternion.Euler(0f, spawnPoint.eulerAngles.y, 0f);
        if (xrActive && xrRoot != null)
        {
            xrRoot.rotation = heading;
            xrRoot.position = spawnPoint.position;
        }
        else
        {
            chosen.transform.SetPositionAndRotation(spawnPoint.position + Vector3.up * desktopEyeHeight, heading);
        }
    }

    private static Transform FindXrRoot(Transform cameraTransform)
    {
        for (Transform t = cameraTransform; t != null; t = t.parent)
            if (t.name.IndexOf("XR Origin", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.name.IndexOf("XR Rig", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return t;
        return null;
    }

    public void Configure(Transform point, float eyeHeight = 1.7f)
    {
        spawnPoint = point;
        desktopEyeHeight = eyeHeight;
    }
}
