using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Management;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;

/// <summary>
/// Chooses desktop or XR mode before any other Awake and places the desktop camera or the XR Origin
/// at the shared exterior start point. XR mode chosen only because an XR loader is running falls back to
/// desktop when no headset display starts. Android players (Quest) have no desktop mode: they always start
/// in XR mode and wait for the headset display.
/// </summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed class ExteriorSpawnPlacement : MonoBehaviour
{
    // Unscaled seconds, counted from the first frame, that a loader-only XR start waits for the headset display.
    private const float HeadsetStartTimeout = 6f;
    // The head is aligned to the spawn once it has been tracked this long, so the XR Origin has applied its
    // tracking origin; a runtime that never reports head tracking is aligned after HeadTrackingTimeout.
    private const float HeadSettleTime = 0.25f;
    private const float HeadTrackingTimeout = 3f;
    private const InputTrackingState HeadPoseTracked = InputTrackingState.Position | InputTrackingState.Rotation;

    private static readonly List<XRDisplaySubsystem> DisplaySubsystems = new List<XRDisplaySubsystem>();
    private static readonly List<XRInputDevice> HeadDevices = new List<XRInputDevice>();

    [SerializeField] private Transform spawnPoint;
    [SerializeField, Min(0.5f)] private float desktopEyeHeight = 1.7f;
    // Foveated rendering strength requested from the headset display (0 = off, 1 = strongest).
    [SerializeField, Range(0f, 1f)] private float xrFoveationLevel = 1f;

    private readonly List<GameObject> hiddenDesktopCameras = new List<GameObject>();
    private bool xrMode;
    private bool waitingForHeadset;
    private bool revertedToDesktop;
    private bool foveationApplied;
    private bool headAligned;
    private float headsetDeadline = -1f;
    private float displayActiveSince = -1f;
    private float headTrackedSince = -1f;
    private Transform xrRig;
    private XROrigin xrOrigin;
    private GameObject handsPermissions;
    private Camera headTrackedDesktopCamera;
    private Transform fallbackTrackingSpace;
    private TrackedPoseDriver addedPoseDriver;
    private Vector3 xrStartPosition;
    private Quaternion xrStartRotation;

    private void Awake()
    {
        ShowroomCollisionSetup.Apply();
        if (spawnPoint == null) spawnPoint = FindDescendant(transform, "Player Spawn");
        if (spawnPoint == null)
            Debug.LogWarning("Exterior spawn 'Player Spawn' was not found; the player keeps its authored pose.", this);

        if (IsHeadsetActive()) EnterXrMode();
        else PlaceDesktopCamera();
    }

    private void Update()
    {
        if (revertedToDesktop)
        {
            if (!XRSettings.isDeviceActive) return;
            revertedToDesktop = false;
            Debug.LogWarning("[Exterior Spawn] The headset display started after the desktop fallback; " +
                             "restart the scene to use XR.", this);
            return;
        }
        if (!xrMode) return;
        if (waitingForHeadset && !HasHeadsetDisplayStarted()) return;
        if (!foveationApplied) foveationApplied = TryEnableFoveation();
        if (xrRig == null) return;
        if (!headAligned) TryAlignHeadToSpawn();

        // Continuous XR locomotion can walk off the city ground.
        if (xrRig.position.y >= ShowroomCollisionSetup.RespawnBelowHeight) return;
        xrRig.SetPositionAndRotation(xrStartPosition, xrStartRotation);
        if (headAligned) AlignHeadToSpawn();
        foreach (GravityProvider gravity in xrRig.GetComponentsInChildren<GravityProvider>())
            gravity.ResetFallForce();
        Physics.SyncTransforms();
    }

    // Android players run on a headset without keyboard or mouse, so the desktop walkthrough is no use there.
    private static bool DesktopModeAvailable => Application.platform != RuntimePlatform.Android;

    private static bool IsHeadsetActive()
    {
        if (XRSettings.isDeviceActive || !DesktopModeAvailable) return true;
        // OpenXR starts its display only once the session is ready, which can be after the first Awake.
        XRGeneralSettings settings = XRGeneralSettings.Instance;
        return settings != null && settings.Manager != null && settings.Manager.activeLoader != null;
    }

    private bool HasHeadsetDisplayStarted()
    {
        if (XRSettings.isDeviceActive)
        {
            waitingForHeadset = false;
            Debug.Log("[Exterior Spawn] The headset display started; staying in XR mode.", this);
            return true;
        }
        // On a headset the display can start late (not worn at launch, a system dialog, a slow first frame);
        // keep waiting there instead of leaving the user in a desktop view they cannot control.
        if (!DesktopModeAvailable) return false;
        if (headsetDeadline < 0f) headsetDeadline = Time.unscaledTime + HeadsetStartTimeout;
        if (Time.unscaledTime >= headsetDeadline) RevertToDesktop();
        return false;
    }

    private void PlaceDesktopCamera()
    {
        Camera camera = FindDesktopCamera();
        if (camera == null) return;
        // The authored camera clears to a solid beige color; outdoors the scene's URP skybox is used.
        camera.clearFlags = CameraClearFlags.Skybox;
        if (spawnPoint != null)
            camera.transform.SetPositionAndRotation(spawnPoint.position + Vector3.up * desktopEyeHeight, SpawnHeading());
    }

    private void EnterXrMode()
    {
        xrMode = true;
        waitingForHeadset = !XRSettings.isDeviceActive;
        if (waitingForHeadset)
            Debug.Log(DesktopModeAvailable
                ? "[Exterior Spawn] An XR loader is active but the headset display has not started yet; " +
                  "XR mode falls back to desktop if it does not start within " + HeadsetStartTimeout + " s."
                : "[Exterior Spawn] Waiting for the headset display; Android players have no desktop mode.", this);

        List<Transform> roots = FindSceneRoots();
        Transform rig = FindXrRig(roots);
        if (rig == null)
        {
            TrackDesktopCamera();
            foveationApplied = TryEnableFoveation();
            return;
        }

        // Exactly one MainCamera and AudioListener: the untracked desktop camera would render over the HMD view.
        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (!camera.transform.IsChildOf(rig) &&
                (camera.CompareTag("MainCamera") || camera.GetComponent<DesktopWalkthroughController>() != null))
            {
                camera.gameObject.SetActive(false);
                hiddenDesktopCameras.Add(camera.gameObject);
            }

        // The right A button advances explorer stages, so it must not also jump.
        foreach (JumpProvider jump in rig.GetComponentsInChildren<JumpProvider>(true))
            jump.enabled = false;

        // The rig's tracking origin is Not Specified: OpenXR uses a floor-level space where it can (real eye
        // height, camera offset 0), otherwise Device mode adds the 1.36 m camera offset. The origin sits on the
        // floor; once the head is tracked, the rig is turned and shifted so the head itself is at the spawn.
        if (spawnPoint != null) rig.SetPositionAndRotation(FloorBelow(spawnPoint.position), SpawnHeading());
        rig.gameObject.SetActive(true);
        xrRig = rig;
        StoreXrStartPose();

        xrOrigin = rig.GetComponentInChildren<XROrigin>(true);
        Camera rigCamera = xrOrigin != null && xrOrigin.Camera != null ? xrOrigin.Camera : rig.GetComponentInChildren<Camera>(true);
        if (rigCamera != null) rigCamera.clearFlags = CameraClearFlags.Skybox;

        // Requests android.permission.HAND_TRACKING on Android XR and starts the hand subsystem once granted.
        foreach (Transform root in roots)
            if (root.name == "Hands Permissions Manager" && !root.gameObject.activeSelf)
            {
                handsPermissions = root.gameObject;
                handsPermissions.SetActive(true);
            }

        foveationApplied = TryEnableFoveation();
    }

    private void RevertToDesktop()
    {
        Debug.LogWarning("[Exterior Spawn] An XR loader is active, but no headset display started within " +
                         HeadsetStartTimeout + " s. Falling back to desktop mode.", this);
        xrMode = false;
        waitingForHeadset = false;
        revertedToDesktop = true;
        if (xrRig != null) xrRig.gameObject.SetActive(false);
        if (handsPermissions != null) handsPermissions.SetActive(false);
        xrRig = null;
        xrOrigin = null;
        handsPermissions = null;

        // The desktop camera was hidden before its first Awake, so its walkthrough starts now as in desktop mode.
        foreach (GameObject cameraObject in hiddenDesktopCameras)
            if (cameraObject != null) cameraObject.SetActive(true);
        hiddenDesktopCameras.Clear();
        if (headTrackedDesktopCamera != null) ReleaseHeadTrackedCamera();
        PlaceDesktopCamera();
    }

    private bool TryEnableFoveation()
    {
#if UNITY_6000_0_OR_NEWER
        SubsystemManager.GetSubsystems(DisplaySubsystems);
        bool applied = false;
        foreach (XRDisplaySubsystem display in DisplaySubsystems)
        {
            if (!display.running) continue;
            // Only the OpenXR Foveated Rendering feature with the SRP foveation API (Android) uses these; other
            // displays ignore them. Without eye tracking or its permission, foveation stays fixed.
            display.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
            display.foveatedRenderingLevel = xrFoveationLevel;
            applied = true;
        }
        return applied;
#else
        return true;
#endif
    }

    private void TryAlignHeadToSpawn()
    {
        if (spawnPoint == null || xrOrigin == null || xrOrigin.Origin == null || xrOrigin.Camera == null)
        {
            headAligned = true;
            return;
        }
        if (!XRSettings.isDeviceActive)
        {
            displayActiveSince = -1f;
            headTrackedSince = -1f;
            return;
        }

        float now = Time.unscaledTime;
        if (displayActiveSince < 0f) displayActiveSince = now;
        if (!IsHeadTracked()) headTrackedSince = -1f;
        else if (headTrackedSince < 0f) headTrackedSince = now;
        bool settled = headTrackedSince >= 0f && now - headTrackedSince >= HeadSettleTime;
        if (!settled && now - displayActiveSince < HeadTrackingTimeout) return;

        Vector3 rigPosition = xrRig.position;
        Quaternion rigRotation = xrRig.rotation;
        AlignHeadToSpawn();
        Physics.SyncTransforms();
        headAligned = true;
        Debug.Log(string.Format("[Exterior Spawn] XR head aligned to '{0}' ({1}): rig moved {2:F2} m and turned {3:F0} deg.",
            spawnPoint.name, settled ? "tracked" : "no tracking reported", Vector3.Distance(rigPosition, xrRig.position),
            Quaternion.Angle(rigRotation, xrRig.rotation)), this);
    }

    private void AlignHeadToSpawn()
    {
        // With a floor-level (stage) space the head starts wherever the user stands in the play area; move the
        // rig so the tracked head, not the rig root, stands at the spawn and looks along the spawn heading.
        // The root keeps its floor height.
        if (spawnPoint != null && xrOrigin != null && xrOrigin.Origin != null && xrOrigin.Camera != null)
        {
            Transform head = xrOrigin.Camera.transform;
            if (Vector3.ProjectOnPlane(head.forward, Vector3.up).sqrMagnitude > 0.01f)
                xrOrigin.MatchOriginUpCameraForward(Vector3.up, SpawnHeading() * Vector3.forward);
            xrOrigin.MoveCameraToWorldLocation(new Vector3(spawnPoint.position.x, head.position.y, spawnPoint.position.z));
        }
        StoreXrStartPose();
    }

    private static bool IsHeadTracked()
    {
        InputDevices.GetDevicesAtXRNode(XRNode.Head, HeadDevices);
        foreach (XRInputDevice head in HeadDevices)
        {
            if (head.TryGetFeatureValue(XRCommonUsages.isTracked, out bool tracked) && tracked) return true;
            if (head.TryGetFeatureValue(XRCommonUsages.trackingState, out InputTrackingState state) &&
                (state & HeadPoseTracked) == HeadPoseTracked) return true;
        }
        return false;
    }

    private void StoreXrStartPose()
    {
        // The y < -25 respawn returns the rig root to this pose.
        xrStartPosition = xrRig.position;
        xrStartRotation = xrRig.rotation;
    }

    private void TrackDesktopCamera()
    {
        Camera camera = FindDesktopCamera();
        if (camera == null)
        {
            Debug.LogWarning("XR is active, but neither an XR rig nor a desktop camera was found.", this);
            return;
        }
        Debug.LogWarning("XR is active, but no 'XR Origin' / 'XR Rig' root was found. Falling back to a head-tracked " +
                         "desktop camera without locomotion or controller interaction.", this);

        DesktopWalkthroughController walkthrough = camera.GetComponent<DesktopWalkthroughController>();
        if (walkthrough != null) walkthrough.enabled = false;
        camera.clearFlags = CameraClearFlags.Skybox;
        headTrackedDesktopCamera = camera;

        // The pose driver writes the local pose, so a floor-level parent puts the tracked head at the spawn.
        Transform cameraTransform = camera.transform;
        fallbackTrackingSpace = new GameObject("Fallback Head Tracking Space").transform;
        fallbackTrackingSpace.SetPositionAndRotation(
            FloorBelow(spawnPoint != null ? spawnPoint.position : cameraTransform.position),
            spawnPoint != null ? SpawnHeading() : Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f));
        cameraTransform.SetParent(fallbackTrackingSpace, false);
        cameraTransform.localPosition = Vector3.zero;
        cameraTransform.localRotation = Quaternion.identity;

        if (camera.GetComponent<TrackedPoseDriver>() != null) return;
        TrackedPoseDriver driver = camera.gameObject.AddComponent<TrackedPoseDriver>();
        driver.positionInput = new InputActionProperty(new InputAction("Head Position",
            binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        driver.rotationInput = new InputActionProperty(new InputAction("Head Rotation",
            binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        driver.trackingStateInput = new InputActionProperty(new InputAction("Head Tracking State",
            binding: "<XRHMD>/trackingState", expectedControlType: "Integer"));
        addedPoseDriver = driver;
    }

    private void ReleaseHeadTrackedCamera()
    {
        // Undoes TrackDesktopCamera; the walkthrough was disabled before its Start, so enabling it starts it.
        Transform cameraTransform = headTrackedDesktopCamera.transform;
        cameraTransform.SetParent(null, true);
        if (fallbackTrackingSpace != null) Destroy(fallbackTrackingSpace.gameObject);
        if (addedPoseDriver != null)
        {
            addedPoseDriver.enabled = false;
            Destroy(addedPoseDriver);
        }
        DesktopWalkthroughController walkthrough = headTrackedDesktopCamera.GetComponent<DesktopWalkthroughController>();
        if (walkthrough != null) walkthrough.enabled = true;
        headTrackedDesktopCamera = null;
        fallbackTrackingSpace = null;
        addedPoseDriver = null;
    }

    private static Camera FindDesktopCamera()
    {
        DesktopWalkthroughController walkthrough = FindAnyObjectByType<DesktopWalkthroughController>();
        Camera camera = walkthrough != null ? walkthrough.GetComponent<Camera>() : null;
        return camera != null ? camera : Camera.main;
    }

    private List<Transform> FindSceneRoots()
    {
        // Includes inactive roots; loaded prefab assets are excluded by the scene check.
        List<Transform> roots = new List<Transform>();
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
            if (candidate != null && candidate.parent == null && candidate.gameObject.scene == gameObject.scene)
                roots.Add(candidate);
        return roots;
    }

    private static Transform FindXrRig(List<Transform> roots)
    {
        Transform named = null;
        foreach (Transform root in roots)
        {
            if (root.name.IndexOf("XR Origin", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                root.name.IndexOf("XR Rig", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (root.GetComponentInChildren<XROrigin>(true) != null) return root;
            if (named == null) named = root;
        }
        return named;
    }

    private static Transform FindDescendant(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform found = FindDescendant(child, childName);
            if (found != null) return found;
        }
        return null;
    }

    private static Vector3 FloorBelow(Vector3 point)
    {
        if (ShowroomCollisionSetup.TryGetFloorHeight(point + Vector3.up * 2f, 8f, out float floorY)) point.y = floorY;
        return point;
    }

    private Quaternion SpawnHeading()
    {
        return Quaternion.Euler(0f, spawnPoint.eulerAngles.y, 0f);
    }

    public void Configure(Transform point, float eyeHeight = 1.7f)
    {
        spawnPoint = point;
        desktopEyeHeight = eyeHeight;
    }
}
