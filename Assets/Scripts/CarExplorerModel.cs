using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The Car Explorer's three views of the hero car.</summary>
public enum CarExplorerView { Normal = 0, XRay = 1, Exploded = 2 }

/// <summary>
/// One node of the hero car's structure tree as the HUD shows it: the vehicle, a system, an assembly, a sub-assembly
/// or a part. Built by the explorer; the HUD only reads it.
/// </summary>
public sealed class CarStructureNode
{
    public string Id;
    public CarNodeKind Kind;
    public string NameEnglish;
    public string NameUzbek;
    public string DescriptionEnglish;
    public string DescriptionUzbek;
    /// <summary>English system name (CarSystems); empty for the vehicle.</summary>
    public string SystemName;
    /// <summary>Main material in English, or empty.</summary>
    public string Material;
    public CarStructureNode Parent;
    public readonly List<CarStructureNode> Children = new List<CarStructureNode>();
    /// <summary>Number of parts (leaves) under this node; 1 for a part.</summary>
    public int PartCount;
    /// <summary>0 for the vehicle, 1 for systems, 2 for assemblies, and so on.</summary>
    public int Depth;
    /// <summary>True when the node can come apart one more level (it has children that move).</summary>
    public bool CanOpen;

    public string Name(bool uzbek) => uzbek && !string.IsNullOrEmpty(NameUzbek) ? NameUzbek : NameEnglish;
    public string Description(bool uzbek) =>
        uzbek && !string.IsNullOrEmpty(DescriptionUzbek) ? DescriptionUzbek : DescriptionEnglish;
}

/// <summary>
/// What the Car Explorer's HUD reads and calls. CarExplorerController implements it and registers itself in
/// CarExplorerModels; the HUD never touches the controller's internals.
/// </summary>
public interface ICarExplorerModel
{
    /// <summary>Raised after anything the HUD shows changes: view, open path, selection, language, motion, greeting.</summary>
    event Action Changed;

    /// <summary>The vehicle node (systems as its children), or null until the tree is built.</summary>
    CarStructureNode Root { get; }
    CarExplorerView View { get; }
    /// <summary>The assembly that has come out of the car (as one unit or apart), or null.</summary>
    CarStructureNode TakenOut { get; }
    /// <summary>
    /// Nodes that have come apart (their children spread), outermost first. When not empty it starts with TakenOut:
    /// [engine, cylinder head, valve train]. Empty while the taken-out assembly is still in one piece.
    /// </summary>
    IReadOnlyList<CarStructureNode> OpenPath { get; }
    /// <summary>The node whose card is shown, or null.</summary>
    CarStructureNode Selected { get; }
    bool Uzbek { get; }
    /// <summary>True while parts or the X-ray fade are moving.</summary>
    bool IsMoving { get; }
    /// <summary>True when the visitor uses the XR rig rather than the desktop walkthrough.</summary>
    bool IsXr { get; }
    /// <summary>The camera the visitor sees through (desktop or XR).</summary>
    Camera ViewCamera { get; }
    /// <summary>True while Remy's welcome should be shown.</summary>
    bool GreetingShown { get; }

    void SetView(CarExplorerView view);
    /// <summary>Highlights the node and shows its card without moving anything.</summary>
    void Select(CarStructureNode node);
    /// <summary>
    /// One step further into the node: an assembly still in the car comes out as one unit; a node that is out, or
    /// already separate from its siblings, comes apart one level. Steps along the way (parents) happen first.
    /// </summary>
    void Open(CarStructureNode node);
    /// <summary>Keeps the first `levels` entries of OpenPath apart and puts the deeper ones back together.</summary>
    void CloseTo(int levels);
    /// <summary>Puts the taken-out assembly back into the car (all its levels close).</summary>
    void PutBack();
    /// <summary>One step back: the deepest apart level closes; then the taken-out assembly goes back; then the view
    /// steps back (exploded, X-ray, normal).</summary>
    void Back();
    /// <summary>Complete vehicle in the normal view.</summary>
    void ResetAll();
    void SetLanguage(bool uzbek);
    /// <summary>Hover highlight from the HUD (null clears it); weaker than the selection highlight.</summary>
    void Highlight(CarStructureNode node);
    /// <summary>
    /// World bounds of the node's parts where they will be when the current move ends (where they are when nothing
    /// moves). False for a node without parts, and for inner content (the V6, the internals) that the current state
    /// does not draw, so there is nothing to frame.
    /// </summary>
    bool TryGetNodeBounds(CarStructureNode node, out Bounds bounds);
    void DismissGreeting();
}

/// <summary>Where the HUD finds the active explorer.</summary>
public static class CarExplorerModels
{
    public static ICarExplorerModel Current { get; private set; }
    public static event Action CurrentChanged;

    /// <summary>
    /// True while the visitor's pointer (the mouse with a free cursor, or an XR controller ray) is over the HUD. Set
    /// by the HUD every frame; the explorer leaves clicks and trigger presses to the HUD while it is true.
    /// </summary>
    public static bool PointerOverHud { get; set; }

    /// <summary>
    /// The part of the desktop view (viewport coordinates) that the HUD's panels leave free. Set by the HUD; the
    /// explorer places taken-out parts inside it. The whole view when no HUD is shown and in XR.
    /// </summary>
    public static Rect HudSafeViewport { get; set; } = new Rect(0f, 0f, 1f, 1f);

    public static void Register(ICarExplorerModel model)
    {
        if (model == null || Current == model) return;
        Current = model;
        CurrentChanged?.Invoke();
    }

    public static void Unregister(ICarExplorerModel model)
    {
        if (Current != model) return;
        Current = null;
        CurrentChanged?.Invoke();
    }
}

/// <summary>
/// The desktop inspect camera: it leaves the walking body, frames a node and orbits around it until the visitor
/// walks on. DesktopWalkthroughController drives it; the HUD asks for it and shows its state.
/// </summary>
public static class CarExplorerInspect
{
    /// <summary>True while the camera orbits an inspected node instead of following the walking body.</summary>
    public static bool Active { get; private set; }
    /// <summary>The node being inspected, or null.</summary>
    public static CarStructureNode Target { get; private set; }

    /// <summary>Asks the camera to frame this node (the HUD's Inspect button, a double click).</summary>
    public static event Action<CarStructureNode> Requested;
    /// <summary>Asks the camera to return to the walking body.</summary>
    public static event Action ExitRequested;
    public static event Action ActiveChanged;

    public static void Request(CarStructureNode node) => Requested?.Invoke(node);
    public static void RequestExit() => ExitRequested?.Invoke();

    /// <summary>Called by the camera's owner when inspecting starts, changes target or ends.</summary>
    public static void SetState(bool active, CarStructureNode target)
    {
        if (Active == active && Target == target) return;
        Active = active;
        Target = active ? target : null;
        ActiveChanged?.Invoke();
    }
}
