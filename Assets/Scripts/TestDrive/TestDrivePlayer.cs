using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;

/// <summary>
/// The visitor's body for the test drive, in either mode: the XR Origin (head-tracked, walks with the rig's own
/// locomotion) or the desktop walkthrough's camera. It stands the visitor at a point, seats them in the car with the
/// head at the driver's eye, and gets them out again.
/// </summary>
public sealed class TestDrivePlayer
{
    private XROrigin origin;
    private Transform rig;
    private Transform rigParent;
    private readonly List<Behaviour> pausedBehaviours = new List<Behaviour>();
    private readonly List<CharacterController> pausedControllers = new List<CharacterController>();

    private DesktopWalkthroughController walkthrough;
    private Camera desktopCamera;
    private Transform desktopSeat;
    private float seatYaw, seatPitch;
    private bool chaseCamera;
    private Vector3 chaseVelocity;

    public bool Seated { get; private set; }
    public bool IsXr => origin != null && origin.gameObject.activeInHierarchy && (XRSettings.isDeviceActive || Application.platform == RuntimePlatform.Android);

    public Camera ViewCamera
    {
        get
        {
            if (IsXr && origin.Camera != null) return origin.Camera;
            if (desktopCamera != null) return desktopCamera;
            return Camera.main;
        }
    }

    public Transform Head => ViewCamera != null ? ViewCamera.transform : null;

    /// <summary>Feet position on the floor below the head.</summary>
    public Vector3 Feet
    {
        get
        {
            if (IsXr) return new Vector3(origin.Camera.transform.position.x, rig.position.y, origin.Camera.transform.position.z);
            if (walkthrough != null && walkthrough.Body != null) return walkthrough.Body.position;
            Transform head = Head;
            return head != null ? head.position - Vector3.up * 1.7f : Vector3.zero;
        }
    }

    public void Resolve()
    {
        if (origin == null || !origin.gameObject.activeInHierarchy)
        {
            foreach (XROrigin candidate in Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                origin = candidate;
                rig = candidate.Origin != null ? candidate.Origin.transform : candidate.transform;
                break;
            }
        }
        if (walkthrough == null)
        {
            walkthrough = Object.FindAnyObjectByType<DesktopWalkthroughController>(FindObjectsInactive.Include);
            if (walkthrough != null) desktopCamera = walkthrough.GetComponent<Camera>();
        }
        if (desktopCamera == null) desktopCamera = Camera.main;
    }

    /// <summary>World position of a controller, from the device pose in the rig's tracking space.</summary>
    public bool TryGetHand(InputAction devicePosition, out Vector3 world)
    {
        world = Vector3.zero;
        if (!IsXr || devicePosition == null || !devicePosition.enabled || devicePosition.activeControl == null) return false;
        Transform space = origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : rig;
        world = space.TransformPoint(devicePosition.ReadValue<Vector3>());
        return true;
    }

    // ------------------------------------------------------------------ standing

    /// <summary>Stands the visitor at <paramref name="feet"/> looking along <paramref name="heading"/>.</summary>
    public void StandAt(Vector3 feet, Vector3 heading)
    {
        heading.y = 0f;
        if (heading.sqrMagnitude < 0.0001f) heading = Vector3.forward;
        heading.Normalize();
        if (Seated) LeaveSeat();
        if (IsXr)
        {
            rig.SetParent(rigParent, true);
            rig.SetPositionAndRotation(new Vector3(rig.position.x, feet.y, rig.position.z), Quaternion.LookRotation(heading, Vector3.up));
            origin.MatchOriginUpCameraForward(Vector3.up, heading);
            Vector3 head = origin.Camera.transform.position;
            origin.MoveCameraToWorldLocation(new Vector3(feet.x, head.y, feet.z));
            Vector3 p = rig.position;
            rig.position = new Vector3(p.x, feet.y, p.z);
            foreach (GravityProvider gravity in rig.GetComponentsInChildren<GravityProvider>())
                gravity.ResetFallForce();
        }
        else if (walkthrough != null)
        {
            walkthrough.PlaceBody(feet, Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg);
        }
        else if (Head != null)
        {
            Head.SetPositionAndRotation(feet + Vector3.up * 1.7f, Quaternion.LookRotation(heading, Vector3.up));
        }
        Physics.SyncTransforms();
    }

    // ------------------------------------------------------------------ seat

    public void Seat(TestDriveVehicle vehicle)
    {
        if (Seated) return;
        Seated = true;
        chaseCamera = false;
        if (IsXr)
        {
            rigParent = rig.parent;
            pausedBehaviours.Clear();
            foreach (LocomotionProvider provider in rig.GetComponentsInChildren<LocomotionProvider>())
                if (provider.enabled) { provider.enabled = false; pausedBehaviours.Add(provider); }
            pausedControllers.Clear();
            foreach (CharacterController controller in rig.GetComponentsInChildren<CharacterController>())
                if (controller.enabled) { controller.enabled = false; pausedControllers.Add(controller); }
            rig.SetParent(vehicle.transform, true);
            Recenter(vehicle);
        }
        else if (desktopCamera != null)
        {
            if (walkthrough != null) walkthrough.enabled = false;
            desktopSeat = vehicle.EyeAnchor;
            desktopCamera.transform.SetParent(desktopSeat, false);
            desktopCamera.transform.localPosition = Vector3.zero;
            seatYaw = 0f;
            seatPitch = 6f;
            desktopCamera.transform.localRotation = Quaternion.Euler(seatPitch, seatYaw, 0f);
            desktopCamera.nearClipPlane = 0.03f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>Puts the head at the driver's eye, looking down the car.</summary>
    public void Recenter(TestDriveVehicle vehicle)
    {
        if (vehicle == null) return;
        if (IsXr && Seated)
        {
            origin.MatchOriginUpCameraForward(vehicle.transform.up, vehicle.transform.forward);
            origin.MoveCameraToWorldLocation(vehicle.EyeAnchor.position);
        }
        else if (!IsXr && Seated && desktopCamera != null && !chaseCamera)
        {
            seatYaw = 0f;
            seatPitch = 6f;
            desktopCamera.transform.localRotation = Quaternion.Euler(seatPitch, seatYaw, 0f);
        }
    }

    private void LeaveSeat()
    {
        Seated = false;
        if (IsXr)
        {
            rig.SetParent(rigParent, true);
            Vector3 forward = Vector3.ProjectOnPlane(rig.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            rig.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            for (int i = 0; i < pausedControllers.Count; i++) if (pausedControllers[i] != null) pausedControllers[i].enabled = true;
            for (int i = 0; i < pausedBehaviours.Count; i++) if (pausedBehaviours[i] != null) pausedBehaviours[i].enabled = true;
            pausedControllers.Clear();
            pausedBehaviours.Clear();
        }
        else if (desktopCamera != null)
        {
            desktopCamera.nearClipPlane = 0.1f;
            if (walkthrough != null)
            {
                walkthrough.enabled = true;
                walkthrough.ReattachCamera();
            }
            else desktopCamera.transform.SetParent(null, true);
        }
    }

    /// <summary>Desktop mouse look in the seat, and the V chase camera. Call every frame while seated.</summary>
    public void UpdateDesktopSeat(TestDriveInput input, TestDriveVehicle vehicle)
    {
        if (IsXr || !Seated || desktopCamera == null) return;
        if (TestDriveInput.Pressed(input.Camera))
        {
            chaseCamera = !chaseCamera;
            if (chaseCamera) desktopCamera.transform.SetParent(null, true);
            else
            {
                desktopCamera.transform.SetParent(desktopSeat, false);
                desktopCamera.transform.localPosition = Vector3.zero;
            }
        }
        Mouse mouse = Mouse.current;
        Vector2 delta = mouse != null && Cursor.lockState == CursorLockMode.Locked ? mouse.delta.ReadValue() * 0.08f : Vector2.zero;
        if (chaseCamera)
        {
            Transform car = vehicle.transform;
            Vector3 flatForward = Vector3.ProjectOnPlane(car.forward, Vector3.up).normalized;
            Vector3 target = car.position - flatForward * (vehicle.HalfLength + 4.2f) + Vector3.up * 2.1f;
            desktopCamera.transform.position = Vector3.SmoothDamp(desktopCamera.transform.position, target, ref chaseVelocity, 0.12f);
            desktopCamera.transform.rotation = Quaternion.LookRotation(car.position + Vector3.up * 1.1f + flatForward * 2f - desktopCamera.transform.position, Vector3.up);
            return;
        }
        seatYaw = Mathf.Clamp(seatYaw + delta.x, -120f, 120f);
        seatPitch = Mathf.Clamp(seatPitch - delta.y, -45f, 50f);
        desktopCamera.transform.localRotation = Quaternion.Euler(seatPitch, seatYaw, 0f);
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = locked;
        }
    }
}
