using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Every control of the test drive, on the Meta Quest Touch controllers, a keyboard and a gamepad.
///
/// Quest: right trigger = accelerator, left trigger = brake, left (or right) thumbstick = steering, right grip =
/// handbrake, A = drive / reverse (outside: open the door, get in), B = get out, Y = horn, X = headlights, left
/// thumbstick click = recentre the seat, hold the menu button = back to the showroom.
/// Keyboard: W/S or arrows, A/D steer, Space handbrake, R drive / reverse, E door / get in / get out, H horn,
/// L lights, C recentre, V camera, G test-drive the car you look at, hold Backspace = showroom.
/// </summary>
public sealed class TestDriveInput
{
    public readonly InputAction Throttle, Brake, Steer, Handbrake, Gear, Interact, Exit, Horn, Lights, Recenter,
        Menu, LeftGrip, RightGrip, LeftHandPosition, RightHandPosition, SelectCar, Camera;

    private readonly List<InputAction> all = new List<InputAction>();

    public TestDriveInput()
    {
        Throttle = Value("Throttle", "<XRController>{RightHand}/trigger", "<Keyboard>/w", "<Keyboard>/upArrow", "<Gamepad>/rightTrigger");
        Brake = Value("Brake", "<XRController>{LeftHand}/trigger", "<Keyboard>/s", "<Keyboard>/downArrow", "<Gamepad>/leftTrigger");
        Steer = Value("Steer", "<XRController>{LeftHand}/thumbstick/x", "<XRController>{RightHand}/thumbstick/x", "<Gamepad>/leftStick/x");
        Steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
        Steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
        Handbrake = Button("Handbrake", "<XRController>{RightHand}/grip", "<Keyboard>/space", "<Gamepad>/buttonEast");
        Gear = Button("Gear", "<XRController>{RightHand}/primaryButton", "<Keyboard>/r", "<Gamepad>/buttonNorth");
        Interact = Button("Interact", "<XRController>{RightHand}/primaryButton", "<Keyboard>/e", "<Gamepad>/buttonSouth");
        Exit = Button("Exit", "<XRController>{RightHand}/secondaryButton", "<Keyboard>/e", "<Gamepad>/buttonWest");
        Horn = Button("Horn", "<XRController>{LeftHand}/secondaryButton", "<Keyboard>/h", "<Gamepad>/leftStickPress");
        Lights = Button("Lights", "<XRController>{LeftHand}/primaryButton", "<Keyboard>/l", "<Gamepad>/dpad/up");
        Recenter = Button("Recenter", "<XRController>{LeftHand}/thumbstickClicked", "<Keyboard>/c", "<Gamepad>/rightStickPress");
        Menu = Button("Showroom", "<XRController>{LeftHand}/menuButton", "<XRController>{LeftHand}/menu", "<Keyboard>/backspace", "<Gamepad>/start");
        LeftGrip = Value("Left Grip", "<XRController>{LeftHand}/grip");
        RightGrip = Value("Right Grip", "<XRController>{RightHand}/grip");
        LeftHandPosition = Pose("Left Hand Position", "<XRController>{LeftHand}/devicePosition");
        RightHandPosition = Pose("Right Hand Position", "<XRController>{RightHand}/devicePosition");
        SelectCar = Button("Select Car", "<Keyboard>/g");
        Camera = Button("Camera", "<Keyboard>/v");
    }

    public void Enable()
    {
        for (int i = 0; i < all.Count; i++) all[i].Enable();
    }

    public void Disable()
    {
        for (int i = 0; i < all.Count; i++) all[i].Disable();
    }

    public void Dispose()
    {
        for (int i = 0; i < all.Count; i++) all[i].Dispose();
        all.Clear();
    }

    public static float Read(InputAction action) => action.enabled ? Mathf.Clamp(action.ReadValue<float>(), -1f, 1f) : 0f;

    public static bool Pressed(InputAction action) => action.enabled && action.WasPressedThisFrame();

    public static bool Held(InputAction action) => action.enabled && action.IsPressed();

    private InputAction Value(string name, params string[] bindings)
    {
        var action = new InputAction("Test Drive " + name, InputActionType.Value, expectedControlType: "Axis");
        for (int i = 0; i < bindings.Length; i++) action.AddBinding(bindings[i]);
        all.Add(action);
        return action;
    }

    private InputAction Button(string name, params string[] bindings)
    {
        var action = new InputAction("Test Drive " + name, InputActionType.Button);
        for (int i = 0; i < bindings.Length; i++) action.AddBinding(bindings[i]);
        all.Add(action);
        return action;
    }

    private InputAction Pose(string name, string binding)
    {
        var action = new InputAction("Test Drive " + name, InputActionType.Value, binding, expectedControlType: "Vector3");
        all.Add(action);
        return action;
    }
}
