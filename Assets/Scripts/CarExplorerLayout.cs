using System;
using System.Collections.Generic;
using UnityEngine;

// Where things go: a taken-out assembly's spot, how a node comes apart one level, the exploded layout of every
// assembly, and the moves between them. Every part's transform is a motion track; its world offset is the sum of the
// offsets of the tree nodes above it (the assembly's move, then each level's spread), so any level can open or close
// while the others hold.
public sealed partial class CarExplorerController
{
    // ---------------------------------------------------------------- assemblies and tracks

    private void SetUpAssembly(Node assembly)
    {
        assembly.RestBounds = GetWorldBoundsOfParts(assembly.AllParts);
        Vector3 fromCenter = assembly.RestBounds.center - carRestCenter;
        float across = Vector3.Dot(fromCenter, carRight);
        bool sided = assembly.Kind == AssemblyKind.Wheel || assembly.Kind == AssemblyKind.Door ||
                     assembly.Kind == AssemblyKind.Generic;
        assembly.Side = sided && Mathf.Abs(across) > 0.2f ? Mathf.Sign(across) : 0f;
        bool inner = assembly.AllParts.Count > 0;
        foreach (Part part in assembly.AllParts)
            inner &= part.IsInner;
        assembly.Inner = inner;
        assembly.Enclosed = inner || assembly.Kind == AssemblyKind.Engine || assembly.Kind == AssemblyKind.Seats ||
                            assembly.Kind == AssemblyKind.Cockpit || assembly.Kind == AssemblyKind.BodyFrame ||
                            assembly.Kind == AssemblyKind.Floor;
        assembly.Relocates = assembly.Kind != AssemblyKind.Floor && assembly.Kind != AssemblyKind.Chassis;
        assembly.ExplodeDelay = GetExplodeDelay(assembly);
        assembly.InBay = Vector3.Dot(fromCenter, carForward) > 0f;
        if (assembly.Kind == AssemblyKind.Wheel)
            assembly.Axle = GetWheelAxle(assembly);
        if (inner)
            CaptureInnerRendering(assembly);
    }

    // The wheel's own axle (the front wheels are steered), found as the thin axis of the tyre's mesh, pointing
    // outward.
    private Vector3 GetWheelAxle(Node assembly)
    {
        Vector3 outward = carRight * (assembly.Side != 0f ? assembly.Side : 1f);
        Part tyre = null;
        foreach (Part part in assembly.AllParts)
        {
            string name = part.Target.name;
            if (Has(name, "Rim") || Has(name, "Disc") || Has(name, "Pad") || Has(name, "Caliper"))
                continue;
            tyre = part;
            break;
        }
        if (tyre == null && assembly.AllParts.Count > 0)
            tyre = assembly.AllParts[0];
        if (tyre == null)
            return outward;
        Transform wheel = tyre.Target.parent != null && tyre.Target.parent != carRoot
            ? tyre.Target.parent : tyre.Target;
        Vector3 axle = outward;
        if (TryGetLocalGeometryBounds(wheel, tyre.Renderers, out Bounds local))
        {
            Vector3 size = local.size;
            Vector3 localAxis = size.x <= size.y && size.x <= size.z ? Vector3.right
                : size.y <= size.z ? Vector3.up : Vector3.forward;
            axle = wheel.TransformDirection(localAxis).normalized;
        }
        return Vector3.Dot(axle, outward) >= 0f ? axle : -axle;
    }

    // One track per part transform (the car root, which carries everything, never moves). A track's owner is its
    // part's leaf node; tracks are updated parents first.
    private void BuildTracks()
    {
        foreach (Part part in parts)
        {
            Transform node = part.Target;
            if (node == carRoot || trackByTransform.ContainsKey(node))
                continue;
            MotionTrack track = new MotionTrack
            {
                Node = node,
                Owner = part.Leaf,
                Assembly = part.Assembly,
                RestLocalPosition = node.localPosition,
                RestLocalRotation = node.localRotation,
                RestLocalScale = node.localScale,
                RestWorldPosition = node.position,
                ParentWorldToLocal = node.parent != null ? node.parent.worldToLocalMatrix : Matrix4x4.identity,
                ParentRestRotation = node.parent != null ? node.parent.rotation : Quaternion.identity,
                FromRotationResidual = Quaternion.identity
            };
            trackByTransform.Add(node, track);
            motionTracks.Add(track);
        }
        foreach (MotionTrack track in motionTracks)
        {
            for (Transform ancestor = track.Node.parent; ancestor != null && ancestor != carRoot.parent;
                 ancestor = ancestor.parent)
                if (trackByTransform.TryGetValue(ancestor, out MotionTrack parentTrack))
                {
                    track.ParentTrack = parentTrack;
                    break;
                }
        }
        motionTracks.Sort((a, b) => GetTransformDepth(a.Node).CompareTo(GetTransformDepth(b.Node)));
        // A part hung under another assembly's part moves with its own assembly but cannot turn with it (only an
        // assembly's top parts turn): such an assembly keeps its heading when it comes out (GetFocusYaw).
        foreach (MotionTrack track in motionTracks)
            if (track.Assembly != null && track.ParentTrack != null && track.ParentTrack.Assembly != track.Assembly)
                track.Assembly.TurnBlocked = true;
    }

    // ---------------------------------------------------------------- moves

    private bool IsOpen(Node node)
    {
        for (int i = 0; i < openPath.Count; i++)
            if (openPath[i] == node)
                return true;
        return false;
    }

    // The offset a node is heading for in the current state (relative to its parent).
    private Vector3 GetPlannedOffset(Node node)
    {
        if (node.Assembly == null)
            return Vector3.zero;
        if (node == node.Assembly)
            return view == CarExplorerView.Exploded ? node.ExplodeOffset
                : node == takenOut ? node.FocusOffset : Vector3.zero;
        if (!IsOpen(node.Parent))
            return Vector3.zero;
        return IsOpen(node) ? node.SpreadOffset + node.PullOffset : node.SpreadOffset;
    }

    private Vector3 GetPlannedWorld(Node node)
    {
        Vector3 world = Vector3.zero;
        for (; node != null; node = node.Parent)
            world += GetPlannedOffset(node);
        return world;
    }

    // World bounds of the node's parts in the planned state, relative to its assembly's own move (that is, as if
    // the assembly stood in the car), or including it.
    private Bounds GetPlannedBounds(Node node, bool includeAssemblyMove)
    {
        Node assembly = node.Assembly;
        Vector3 assemblyMove = assembly != null && !includeAssemblyMove ? GetPlannedOffset(assembly) : Vector3.zero;
        bool found = false;
        Bounds result = node.RestBounds;
        foreach (Part part in node.AllParts)
        {
            Vector3 move = GetPlannedWorld(part.Leaf) - assemblyMove;
            foreach (Renderer renderer in part.Renderers)
            {
                if (renderer == null) continue;
                Bounds bounds = GetRestRendererBounds(part, renderer);
                bounds.center += move;
                if (!found) { result = bounds; found = true; }
                else result.Encapsulate(bounds);
            }
        }
        return result;
    }

    // A renderer's world bounds with its part at rest (bounds read now would include a move under way).
    private Bounds GetRestRendererBounds(Part part, Renderer renderer)
    {
        Bounds bounds = renderer.bounds;
        if (trackByTransform.TryGetValue(part.Target, out MotionTrack track))
            bounds.center += track.RestWorldPosition - part.Target.position;
        return bounds;
    }

    // Every node heads from where it is now to its planned offset; the tracks follow, from wherever a grab or an
    // interrupted move left them. Held parts are let go first; new grabs wait until the move ends (partGrabFilter).
    private void BeginMotion(float duration, bool staggered, bool collapsing)
    {
        ReleaseGrabbedParts();
        UpdateWorldOffsets();
        float latest = 0f;
        foreach (Node assembly in assemblies)
            latest = Mathf.Max(latest, assembly.ExplodeDelay);
        float maxDelay = 0f;
        bool travel = false, spread = false, close = false;
        movingNodes.Clear();
        foreach (Node node in allNodes)
        {
            if (node.Assembly == null)
                continue;
            Node assembly = node.Assembly;
            float delay = !staggered ? 0f : collapsing ? latest - assembly.ExplodeDelay : assembly.ExplodeDelay;
            maxDelay = Mathf.Max(maxDelay, delay);
            node.Delay = delay;
            node.Target = GetPlannedOffset(node);
            if (node == assembly)
            {
                node.TargetYaw = view != CarExplorerView.Exploded && node == takenOut ? node.FocusYaw : 0f;
                if (node.TargetYaw != 0f)
                    node.YawPivot = node.FocusPivot;
            }
            node.From = node.Offset;
            node.FromYaw = node.Yaw;
            node.Moving = node.From != node.Target || node.FromYaw != node.TargetYaw;
            node.Spreading = node.Target.sqrMagnitude >= node.From.sqrMagnitude;
            if (!node.Moving)
                continue;
            movingNodes.Add(node);
            if (node == assembly) travel = true;
            else if (node.Spreading) spread = true;
            else close = true;
        }
        // In the exploded view's staggered moves an assembly travels first and comes apart on the way; coming back,
        // it closes first and then travels home. Every other move runs its kinds one after another (SetMoveWindows).
        phasedMotion = (travel && (spread || close)) || (spread && close);
        windowedMotion = !staggered;
        if (windowedMotion)
            duration = SetMoveWindows(duration);

        movingTracks.Clear();
        foreach (MotionTrack track in motionTracks)
        {
            if (track.Node == null)
                continue;
            track.FromResidual = track.Node.localPosition - GetTrackLocalPosition(track);
            track.FromRotationResidual = Quaternion.Inverse(GetTrackLocalRotation(track)) * track.Node.localRotation;
            track.FromScale = track.Node.localScale;
            bool moving = track.FromResidual.sqrMagnitude > 1e-10f ||
                          track.FromRotationResidual != Quaternion.identity || track.FromScale != track.RestLocalScale;
            for (Node node = track.Owner; node != null && !moving; node = node.Parent)
                moving = node.Moving;
            track.Moving = moving;
            if (moving)
                movingTracks.Add(track);
        }
        transitionLength = Mathf.Max(0.1f, duration);
        StartPendingStepBack();
        transitionSpan = Mathf.Max(0.3f, transitionLength - maxDelay);
        transitionClock = 0f;
        transitionT = movingNodes.Count > 0 || movingTracks.Count > 0 ? 0f : 1f;
        if (transitionT >= 1f)
            FinishTransition();
    }

    // Outside the exploded view's staggered moves, the kinds of move run one after another, each in its own stretch of
    // the time: open levels close, an assembly goes home, an assembly comes out (or moves to a new place), levels
    // open; so an assembly travels closed and its levels never sweep through the car. An assembly that travels gets
    // its path (BuildPath) and MoveTravelWeight of the move's duration (MoveTravelSharedWeight each when one goes home
    // while another comes out), a level move MoveLevelWeight; a move of levels alone keeps the duration. Returns the
    // move's length.
    private float SetMoveWindows(float duration)
    {
        bool closing = false, home = false, outward = false, opening = false, path = false;
        foreach (Node node in movingNodes)
        {
            node.WindowStart = 0f;
            node.WindowEnd = 1f;
            if (node == node.Assembly)
            {
                BuildPath(node);
                path |= node.PathCount > 0;
                if (node.Target == Vector3.zero && node.TargetYaw == 0f)
                    home = true;
                else
                    outward = true;
            }
            else if (node.Spreading)
                opening = true;
            else
                closing = true;
        }
        if (!home && !outward)
        {
            if (!(closing && opening))
                return duration;
        }
        float travel = home && outward ? MoveTravelSharedWeight : path ? MoveTravelWeight : 1f;
        float level = home || outward ? MoveLevelWeight : 0.5f;
        float closeEnd = closing ? level : 0f;
        float homeEnd = closeEnd + (home ? travel : 0f);
        float outEnd = homeEnd + (outward ? travel : 0f);
        float total = outEnd + (opening ? level : 0f);
        foreach (Node node in movingNodes)
        {
            float start, end;
            if (node == node.Assembly)
            {
                bool goingHome = node.Target == Vector3.zero && node.TargetYaw == 0f;
                start = goingHome ? closeEnd : homeEnd;
                end = goingHome ? homeEnd : outEnd;
            }
            else if (node.Spreading)
            {
                start = outEnd;
                end = total;
            }
            else
            {
                start = 0f;
                end = closeEnd;
            }
            node.WindowStart = start / total;
            node.WindowEnd = Mathf.Max(node.WindowStart + 1e-3f, end / total);
        }
        return duration * total;
    }

    // node.World: the node's offset plus its parents' (the vehicle and the systems never move).
    private void UpdateWorldOffsets()
    {
        foreach (Node node in allNodes)
            node.World = node.Parent != null ? node.Parent.World + node.Offset : node.Offset;
    }

    // The track's local position for its owner's offsets: rest pose plus its offset relative to its tracked parent,
    // or, for a top track of a turned assembly, its turned and moved world position in its (static) parent's space.
    private static Vector3 GetTrackLocalPosition(MotionTrack track)
    {
        Node assembly = track.Assembly;
        Vector3 world = track.Owner.World;
        if (assembly != null && assembly.Yaw != 0f && track.ParentTrack == null)
        {
            Vector3 turned = assembly.YawPivot + Quaternion.AngleAxis(assembly.Yaw, Vector3.up) *
                (track.RestWorldPosition + (world - assembly.World) - assembly.YawPivot) + assembly.World;
            return track.ParentWorldToLocal.MultiplyPoint3x4(turned);
        }
        if (track.ParentTrack != null)
            world -= track.ParentTrack.Owner.World;
        return track.RestLocalPosition + track.ParentWorldToLocal.MultiplyVector(world);
    }

    private static Quaternion GetTrackLocalRotation(MotionTrack track)
    {
        Node assembly = track.Assembly;
        if (assembly == null || assembly.Yaw == 0f || track.ParentTrack != null)
            return track.RestLocalRotation;
        // The world turn expressed in the parent's space, then the authored local rotation.
        return Quaternion.Inverse(track.ParentRestRotation) * Quaternion.AngleAxis(assembly.Yaw, Vector3.up) *
               track.ParentRestRotation * track.RestLocalRotation;
    }

    private static float Smooth(float t)
    {
        return t * t * (3f - 2f * t);
    }

    private void AnimateParts()
    {
        if (transitionT >= 1f)
            return;
        transitionClock += Time.deltaTime;
        transitionT = Mathf.Clamp01(transitionClock / transitionLength);
        foreach (Node node in movingNodes)
        {
            float local = Mathf.Clamp01((transitionClock - node.Delay) / transitionSpan);
            float amount = Smooth(local);
            if (windowedMotion)
                amount = Smooth(Mathf.Clamp01((local - node.WindowStart) /
                                              Mathf.Max(1e-4f, node.WindowEnd - node.WindowStart)));
            else if (phasedMotion)
            {
                float first = Smooth(Mathf.Clamp01(local / FirstPhaseShare));
                float second = Smooth(Mathf.Clamp01((local - SecondPhaseStart) / (1f - SecondPhaseStart)));
                bool isAssembly = node == node.Assembly;
                amount = isAssembly == node.Spreading ? first : second;
            }
            if (node.PathCount > 0)
            {
                // A taken-out assembly's path: out of its place, over the car and down (BuildPath).
                node.Offset = EvaluatePath(node, amount);
                node.Yaw = Mathf.LerpUnclamped(node.FromYaw, node.TargetYaw, GetPathTurn(node, amount));
                continue;
            }
            node.Offset = Vector3.LerpUnclamped(node.From, node.Target, amount);
            if (node == node.Assembly)
                node.Yaw = Mathf.LerpUnclamped(node.FromYaw, node.TargetYaw, amount);
        }
        UpdateWorldOffsets();
        float settle = Smooth(Mathf.Clamp01(transitionClock / transitionSpan));
        foreach (MotionTrack track in movingTracks)
        {
            if (track.Node == null)
                continue;
            track.Node.localPosition = GetTrackLocalPosition(track) + track.FromResidual * (1f - settle);
            track.Node.localRotation = GetTrackLocalRotation(track) *
                                       Quaternion.SlerpUnclamped(track.FromRotationResidual, Quaternion.identity, settle);
            track.Node.localScale = Vector3.LerpUnclamped(track.FromScale, track.RestLocalScale, settle);
        }
        if (transitionT >= 1f)
            FinishTransition();
    }

    private void FinishTransition()
    {
        foreach (Node node in movingNodes)
        {
            node.Offset = node.Target;
            node.Yaw = node.TargetYaw;
            node.Moving = false;
            node.PathCount = 0;
        }
        UpdateWorldOffsets();
        foreach (MotionTrack track in movingTracks)
        {
            if (track.Node == null)
                continue;
            track.FromResidual = Vector3.zero;
            track.FromRotationResidual = Quaternion.identity;
            track.Moving = false;
            // Exactly the authored pose at rest (no accumulated float error).
            bool atRest = track.Owner.World == Vector3.zero && (track.Assembly == null || track.Assembly.Yaw == 0f);
            track.Node.localPosition = atRest ? track.RestLocalPosition : GetTrackLocalPosition(track);
            track.Node.localRotation = atRest ? track.RestLocalRotation : GetTrackLocalRotation(track);
            track.Node.localScale = track.RestLocalScale;
        }
        movingNodes.Clear();
        movingTracks.Clear();
        // With nothing out of the car every transform is back in its authored pose, exactly: also one that an
        // interrupted move left a hair off its place, too little to count as moving (BeginMotion).
        if (takenOut == null && view != CarExplorerView.Exploded)
        {
            foreach (MotionTrack track in motionTracks)
            {
                track.FromResidual = Vector3.zero;
                track.FromRotationResidual = Quaternion.identity;
                track.Moving = false;
            }
            RestoreExactTransforms();
        }
        transitionT = 1f;
        UpdateInnerRendering();
        // A node the new state hides (the V6 back under the hood) is no longer explained, highlighted or pointed at.
        if (selected != null && !IsNodeVisible(selected))
            SelectNode(null, null);
        NotifyChanged();
    }

    // At once back to the normal, assembled car in its own materials.
    private void SnapToRest()
    {
        view = CarExplorerView.Normal;
        currentLevel = 0;
        takenOut = null;
        openPath.Clear();
        foreach (Node node in allNodes)
        {
            node.Offset = node.From = node.Target = node.World = Vector3.zero;
            node.Yaw = node.FromYaw = node.TargetYaw = 0f;
            node.Moving = false;
            node.PathCount = 0;
        }
        foreach (MotionTrack track in motionTracks)
        {
            track.FromResidual = Vector3.zero;
            track.FromRotationResidual = Quaternion.identity;
            track.Moving = false;
        }
        movingNodes.Clear();
        movingTracks.Clear();
        // Parts still gliding back from a grab land at once (nothing updates them while the explorer is off).
        foreach (Part part in returningParts)
        {
            if (part.Target == null) continue;
            part.Target.localPosition = part.HomeLocalPosition;
            part.Target.localRotation = part.HomeLocalRotation;
            part.Target.localScale = part.HomeLocalScale;
        }
        returningParts.Clear();
        RestoreExactTransforms();
        transitionT = 1f;
        transitionClock = 0f;
        RestoreAllGhosts();
    }

    private void RestoreExactTransforms()
    {
        foreach (MotionTrack track in motionTracks)
        {
            if (track.Node == null) continue;
            track.Node.localPosition = track.RestLocalPosition;
            track.Node.localRotation = track.RestLocalRotation;
            track.Node.localScale = track.RestLocalScale;
        }
    }

    // ---------------------------------------------------------------- coming apart one level

    // The node's children move apart: the anchor (marked, or the largest child without a direction of its own) stays,
    // the others move along their direction (their CarPartInfo direction, a rule's, the wheel's axle, or from the
    // anchor's centre to theirs) by a distance scaled by the node's size, and further until no two children's boxes
    // come within SiblingGap. Coaxial stacks (a wheel's tyre and rim, disc and caliper) spread along their axle.
    private void ComputeSpread(Node node)
    {
        List<Node> kids = node.Children;
        if (kids.Count < 2)
            return;
        Node anchor = null;
        foreach (Node kid in kids)
            if (kid.Hint.Mode == SpreadMode.Anchor)
            {
                anchor = kid;
                break;
            }
        if (anchor == null)
        {
            float best = -1f;
            foreach (Node kid in kids)
            {
                if (kid.Hint.Mode != SpreadMode.Auto)
                    continue;
                Vector3 size = kid.RestBounds.size;
                float volume = (size.x + 0.01f) * (size.y + 0.01f) * (size.z + 0.01f);
                if (volume > best)
                {
                    best = volume;
                    anchor = kid;
                }
            }
        }
        Vector3 center = anchor != null ? anchor.RestBounds.center : node.RestBounds.center;
        Vector3 nodeSize = node.RestBounds.size;
        float extent = Mathf.Max(nodeSize.x, Mathf.Max(nodeSize.y, nodeSize.z));
        float gap = 0.03f + 0.1f * extent;
        float step = Mathf.Max(0.01f, 0.02f * extent);

        spreadBoxes.Clear();
        spreadOrder.Clear();
        foreach (Node kid in kids)
        {
            if (kid == anchor)
            {
                kid.SpreadOffset = Vector3.zero;
                spreadBoxes.Add(kid.RestBounds);
            }
            else
                spreadOrder.Add(kid);
        }
        spreadOrder.Sort((a, b) => (a.RestBounds.center - center).sqrMagnitude
            .CompareTo((b.RestBounds.center - center).sqrMagnitude));
        foreach (Node kid in spreadOrder)
        {
            Vector3 direction = GetSpreadDirection(node, kid, center, extent);
            float distance = gap * kid.Distance;
            Bounds box = kid.RestBounds;
            for (int i = 0; i < 240; i++)
            {
                Bounds moved = box;
                moved.center += direction * distance;
                if (!OverlapsAny(moved, spreadBoxes, SiblingGap))
                    break;
                distance += step;
            }
            kid.SpreadOffset = direction * distance;
            box.center += kid.SpreadOffset;
            spreadBoxes.Add(box);
        }
    }

    // A level below the assembly that comes apart steps out of the rest of the assembly toward the visitor, so its
    // parts spread in free space within reach of the eye: about PullEyeDistance from the eye (horizontally; further
    // when the level is big, and in the exploded view never more than PullTravelMax from where it was), a little below
    // the eye, in view,
    // and clear of every other drawn part, of the viewer, the room and the keep-outs. Where that place is taken (or out
    // of view) it comes a little nearer, a little higher or lower, a little further, then turns round the eye in steps,
    // first toward the middle of the view. Each deeper level comes out the same way, beside the one before. The
    // taken-out assembly already stands in front of the visitor (ComputeFocusOffset), so its levels come out on the
    // same line, a little nearer, never across the car. While the desktop inspect camera orbits, the eye is the
    // walking eye it left (PreparePlacementView).
    private void ComputePull(Node node)
    {
        node.PullOffset = Vector3.zero;
        Node assembly = node.Assembly;
        if (assembly == null || node == assembly || viewCamera == null)
            return;
        PreparePlacementView();
        Bounds own = GetPlannedBounds(node, true);
        Vector3 eye = placementEye;
        GatherFocusKeepOuts();
        GatherPartBoxes(node);
        float ceiling = view == CarExplorerView.Exploded ? ExplodeMaxHeight : FocusMaxHeight;
        float floor = GetFloorTop() + FloorClearance;
        float lowest = floor + PullMinHeight;
        float highest = Mathf.Max(lowest, eye.y - BelowEye);
        Vector3 toLevel = Vector3.ProjectOnPlane(own.center - eye, Vector3.up);
        float distanceNow = toLevel.magnitude;
        Vector3 look = Vector3.ProjectOnPlane(placementRotation * Vector3.forward, Vector3.up);
        Vector3 bearing = distanceNow > 0.01f ? toLevel / distanceNow
            : look.sqrMagnitude > 0.0001f ? look.normalized : carForward;
        float toward = look.sqrMagnitude > 0.0001f && Vector3.SignedAngle(bearing, look, Vector3.up) < 0f ? -1f : 1f;
        float halfWidth = Mathf.Max(own.extents.x, own.extents.z);
        bool xr = IsXrView();
        float pullClearance = xr ? PullViewerClearanceXr : PullViewerClearance;
        float nearest = pullClearance + halfWidth + 0.05f;
        float reach = Mathf.Max(xr ? PullEyeDistanceXr : PullEyeDistance, nearest + 0.15f, own.extents.magnitude * 3.2f);
        // In the exploded view the level stays near its assembly; a taken-out assembly's comes to the visitor.
        if (view == CarExplorerView.Exploded)
            reach = Mathf.Max(reach, distanceNow - PullTravelMax);
        float baseHeight = Mathf.Clamp(own.center.y, lowest, highest);
        // A taken-out assembly's level stays out of the car and in sight past it, like the assembly.
        bool outside = view != CarExplorerView.Exploded && assembly == takenOut;
        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        for (int t = 0; t < PullTurns.Length * 2 - 1; t++)
        {
            float turn = t == 0 ? 0f : PullTurns[(t + 1) / 2] * ((t & 1) == 1 ? toward : -toward);
            Vector3 direction = Quaternion.AngleAxis(turn, Vector3.up) * bearing;
            for (int n = 0; n < PullDistanceSteps.Length; n++)
            {
                float distance = reach + PullDistanceSteps[n];
                if (n > 0 && distance < nearest)
                    continue;
                for (int h = 0; h < 3; h++)
                {
                    float height = Mathf.Clamp(baseHeight + (h == 1 ? 0.25f : h == 2 ? -0.25f : 0f), lowest, highest);
                    if (h > 0 && Mathf.Abs(height - baseHeight) < 0.01f)
                        continue;
                    Vector3 center = eye + direction * distance;
                    center.y = height;
                    Bounds moved = own;
                    moved.center = center;
                    float score = ScoreView(moved, eye, ceiling, pullClearance, out bool valid) -
                                  0.3f * t - 0.2f * n - 0.1f * h;
                    if (outside && (!IsOutsideFootprint(moved) || !SeesPastCar(moved.center)))
                    {
                        valid = false;
                        score -= 200f;
                    }
                    if (valid)
                    {
                        node.PullOffset = center - own.center;
                        return;
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = center - own.center;
                    }
                }
            }
        }
        node.PullOffset = best;
    }

    private Vector3 GetSpreadDirection(Node node, Node kid, Vector3 center, float extent)
    {
        Node assembly = node.Assembly;
        Vector3 outward = carRight * (assembly != null && assembly.Side != 0f ? assembly.Side : 1f);
        Vector3 direction;
        switch (kid.Hint.Mode)
        {
            case SpreadMode.Direction:
                direction = kid.HintIsWorld ? kid.Hint.Car
                    : outward * kid.Hint.Car.x + Vector3.up * kid.Hint.Car.y + carForward * kid.Hint.Car.z;
                break;
            case SpreadMode.AxleOut:
                direction = assembly != null && assembly.Axle != Vector3.zero ? assembly.Axle : outward;
                break;
            case SpreadMode.AxleIn:
                direction = -(assembly != null && assembly.Axle != Vector3.zero ? assembly.Axle : outward);
                break;
            default:
                direction = kid.RestBounds.center - center;
                // Children on one centre (a coaxial stack: a coilover's spring round its damper) spread along the
                // wheel's axle or the node's long axis.
                if (direction.magnitude < 0.04f * extent)
                    direction = assembly != null && assembly.Axle != Vector3.zero ? assembly.Axle
                        : GetLongAxis(node.RestBounds, kid.RestBounds.center - carRestCenter);
                break;
        }
        return direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.up;
    }

    // The box's longest axis: up when it is tallest, else the horizontal axis that points away from the car's centre.
    private static Vector3 GetLongAxis(Bounds bounds, Vector3 away)
    {
        Vector3 size = bounds.size;
        if (size.y >= size.x && size.y >= size.z)
            return Vector3.up;
        Vector3 axis = size.x >= size.z ? Vector3.right : Vector3.forward;
        return Vector3.Dot(axis, away) >= 0f ? axis : -axis;
    }

    private static bool OverlapsAny(Bounds box, List<Bounds> boxes, float gap)
    {
        box.Expand(gap);
        foreach (Bounds other in boxes)
            if (other.Intersects(box))
                return true;
        return false;
    }

    // The children stay above the floor and under the height limit where the node will stand (called once the
    // assembly's place is settled).
    private void KeepSpreadInRoom(Node node)
    {
        Vector3 at = GetPlannedWorld(node);
        float floor = GetFloorTop() + FloorClearance;
        float ceiling = view == CarExplorerView.Exploded ? ExplodeMaxHeight : FocusMaxHeight;
        foreach (Node kid in node.Children)
        {
            Bounds box = kid.RestBounds;
            box.center += at + kid.SpreadOffset;
            float lift = 0f;
            if (box.max.y > ceiling)
                lift = ceiling - box.max.y;
            if (box.min.y + lift < floor)
                lift = floor - box.min.y;
            if (lift != 0f)
                kid.SpreadOffset += Vector3.up * lift;
        }
    }

    // ---------------------------------------------------------------- taking an assembly out

    // Where a clicked assembly goes, the same rule for every assembly: out of the car, toward the visitor. Its place is
    // looked for on the horizontal line from where it is (its rest centre in the car; where it stands, when it comes
    // again) to the eye, so on screen it comes toward the visitor instead of sliding across the view; only when nothing
    // on that line will do does it turn round the eye, as little as it can (FindVisitorPlace). The hard rules: its box
    // (with every open level) ends outside the car's footprint (IsOutsideFootprint: hovering over the roof or the bonnet
    // counts as inside), the car's box never stands between the eye and its centre (SeesPastCar), its bottom stays off
    // the floor and its box FocusNearClearance (horizontally) from the eye and under FocusFillLimit of the view's
    // height. Then it should be in view (ViewShareRequired of its screen box in the free part of the view:
    // CarExplorerModels.HudSafeViewport on the desktop, XrViewArea in XR) and clear of Remy, the other display cars,
    // furniture and walls. Among those, the turn costs most (FocusTurnCost per degree), then coming nearer or going
    // further than the comfortable distance (about FocusFill of the view's height, FocusEyeDistanceMin..Max from the
    // eye; FocusEyeDistanceMinXr in XR), then the height off the chest band (FocusChestBelowEye..BelowEye under the
    // eye). While the desktop inspect camera orbits, the visitor is the walking eye it left (PreparePlacementView).
    // `spread` is the assembly's box with its open levels, as if it stood in the car; `from` is where it is now
    // (null: in the car).
    private Vector3 ComputeFocusOffset(Node assembly, Bounds spread, Vector3? from = null,
        float maxDistance = float.PositiveInfinity)
    {
        PreparePlacementView();
        EnsureBodyHeights();
        Vector3 viewer = placementEye;
        assembly.FocusPivot = assembly.RestBounds.center;
        assembly.FocusYaw = GetFocusYaw(assembly, assembly.FocusPivot, viewer);
        if (!assembly.Relocates)
            return Vector3.zero;
        spread = RotateBounds(spread, assembly.FocusYaw, assembly.FocusPivot);
        GatherFocusKeepOuts();
        Vector3 source = from ?? spread.center;
        Vector3 place = FindVisitorPlace(spread, source, maxDistance, out int tier);
        if (tier > 0 && TryStepBack(spread, source, maxDistance, out Vector3 stepped))
            place = stepped;
        return place - spread.center;
    }

    // Desktop framing: when no place keeps every rule for the visitor's eye, the walking body may glide straight back
    // from the car (along the line from the car's centre through the eye, keeping its yaw and pitch) during the move,
    // to the nearest eye (StepBackStep apart, up to StepBackMax, as far as the body can go without running into
    // anything) from which one exists; the assembly is placed for that eye (it may end where the visitor stood: they
    // glide away while it comes, and its path keeps clear of both eyes, BuildPath). Never in XR, while inspecting or
    // back from it; without such an eye (a wall behind) nothing glides and the place found for the eye as it is
    // stands.
    private bool TryStepBack(Bounds spread, Vector3 source, float maxDistance, out Vector3 place)
    {
        place = default;
        ICarExplorerStepBackBody stepBody = CarExplorerStepBack.Body;
        if (stepBody == null || !stepBody.CanStepBack || IsXrView() || placementFromWalk || viewCamera == null ||
            CarExplorerInspect.Active)
            return false;
        Vector3 away = Vector3.ProjectOnPlane(placementEye - carRestCenter, Vector3.up);
        if (away.sqrMagnitude < 0.01f)
            return false;
        away.Normalize();
        Vector3 eye = placementEye;
        bool shifted = placementShifted;
        // From where the body will be (with a glide under way, from its end).
        float already = Mathf.Max(0f, Vector3.Dot(eye - placementBodyEye, away));
        float reach = stepBody.GetStepBackReach(away, StepBackMax + already) - already;
        float step = 0f;
        for (int n = 1; n * StepBackStep <= Mathf.Min(reach, StepBackMax) + 0.001f; n++)
        {
            placementEye = eye + away * (n * StepBackStep);
            placementShifted = true;
            Vector3 candidate = FindVisitorPlace(spread, source, maxDistance, out int tier);
            if (tier == 0)
            {
                place = candidate;
                step = n * StepBackStep;
                break;
            }
        }
        placementEye = eye;
        placementShifted = shifted;
        if (step <= 0f)
            return false;
        pendingStepBack += away * step;
        placementEye = eye + away * step;
        placementShifted = true;
        return true;
    }

    // The step back planned for this command starts with its move and lasts as long (the rest of one under way
    // included); the body ends it when the visitor takes over.
    private void StartPendingStepBack()
    {
        if (pendingStepBack.sqrMagnitude < 1e-6f)
            return;
        ICarExplorerStepBackBody stepBody = CarExplorerStepBack.Body;
        if (stepBody != null && stepBody.CanStepBack && !IsXrView())
            stepBody.StepBack(pendingStepBack + stepBody.StepBackRemaining, transitionLength);
        pendingStepBack = Vector3.zero;
    }


    // A place tried for a taken-out assembly: its centre, its tier (0: every rule kept; 1: in view but bigger than
    // FocusFillLimit; 2: out of view; 3: in Remy's, a car's, a wall's or furniture's way; 4: only outside the car and
    // seen past it) and what orders it in the tier.
    private struct FocusCandidate
    {
        public Vector3 Center;
        public int Tier;
        public float Cost;
    }

    // The centre ComputeFocusOffset puts `spread` at. Turns round the eye are tried in FocusTurns order (either way);
    // at each turn every distance (the comfortable one, then nearer in FocusDistanceStep steps down to the near limit,
    // then further) and height; the first turn that has a place keeping every rule ends the search, so the assembly
    // turns no further than it must. Without such a place: one in view though bigger than FocusFillLimit, then the one
    // in view most (then nearest the view's direction), then one in the way of something, then any outside the car and
    // seen past it.
    private Vector3 FindVisitorPlace(Bounds spread, Vector3 source, float maxDistance, out int foundTier)
    {
        foundTier = 5;
        Vector3 eye = placementEye;
        Rect free = GetFreeView();
        Vector3 middle = GetPlacementDirection(free.center);
        Vector3 look = Vector3.ProjectOnPlane(middle, Vector3.up);
        if (look.sqrMagnitude < 0.0025f)
            look = Vector3.ProjectOnPlane(carRestCenter - eye, Vector3.up);
        look = look.sqrMagnitude > 0.0001f ? look.normalized : carForward;
        // The line toward the visitor: from the eye to the assembly (seen from above).
        Vector3 toSource = Vector3.ProjectOnPlane(source - eye, Vector3.up);
        Vector3 line = toSource.magnitude > 0.2f ? toSource.normalized : look;
        float slope = middle.y / Mathf.Max(0.05f, Mathf.Sqrt(middle.x * middle.x + middle.z * middle.z));
        float half = spread.extents.y;
        float lowest = GetFloorTop() + FloorClearance + half;
        float highest = Mathf.Max(lowest, eye.y + FocusTopAboveEye - half);
        bool xr = IsXrView();
        float comfortable = xr ? FocusEyeDistanceMinXr : FocusEyeDistanceMin;
        float clearance = xr ? FocusNearClearanceXr : FocusNearClearance;
        float furthest = Mathf.Max(comfortable, Mathf.Min(FocusEyeDistanceMax + FocusDistanceStretch, maxDistance));
        float preferred = Mathf.Min(furthest,
            GetComfortDistance(spread, line, GetChestHeight(eye, slope, 2f, lowest, highest), comfortable, free));
        float nearest = Mathf.Max(0.3f, clearance + 0.05f);
        float distanceStep = xr ? FocusDistanceStepXr : FocusDistanceStep;

        FocusCandidate best = new FocusCandidate { Tier = 5, Cost = float.PositiveInfinity };
        float lineToLook = Vector3.Angle(line, look);
        float halfView = Mathf.Atan(placementTanX) * Mathf.Rad2Deg;
        for (int t = 0; t < FocusTurns.Length; t++)
        {
            for (int sign = 0; sign < (t == 0 ? 1 : 2); sign++)
            {
                float turn = FocusTurns[t] * (sign == 0 ? 1f : -1f);
                Vector3 direction = Quaternion.AngleAxis(turn, Vector3.up) * line;
                float lookCost = Vector3.Angle(direction, look);
                // Nearer first (down to the near limit), then further.
                int nearerSteps = Mathf.Max(0, Mathf.CeilToInt((preferred - nearest) / distanceStep));
                int furtherSteps = Mathf.Max(0, Mathf.CeilToInt((furthest - preferred) / distanceStep));
                for (int n = 0; n <= nearerSteps + furtherSteps; n++)
                {
                    float distance = n <= nearerSteps
                        ? Mathf.Max(nearest, preferred - n * distanceStep)
                        : Mathf.Min(furthest, preferred + (n - nearerSteps) * distanceStep);
                    Vector3 ground = eye + direction * distance;
                    Bounds target = spread;
                    target.center = new Vector3(ground.x, spread.center.y, ground.z);
                    // Outside the footprint does not depend on the height.
                    if (!IsOutsideFootprint(target))
                        continue;
                    float chest = GetChestHeight(eye, slope, distance, lowest, highest);
                    for (int h = 0; h < FocusLifts.Length; h++)
                    {
                        float height = chest + FocusLifts[h];
                        if (height < lowest - 0.001f || height > highest + 0.001f)
                            continue;
                        target.center = new Vector3(ground.x, height, ground.z);
                        if (!SeesPastCar(target.center))
                            continue;
                        float cost = FocusTurnCost * Mathf.Abs(turn) +
                                     (distance < preferred ? FocusNearerCost : FocusFurtherCost) *
                                     Mathf.Abs(distance - preferred) + FocusLiftCost * Mathf.Abs(FocusLifts[h]);
                        // Tiers: 0 every rule kept; 1 in view but over FocusFillLimit of the view's height (only nearer
                        // than that fits on the line, and no turn finds room in view); 2 out of view (in view most,
                        // then nearest the view's direction); 3 in a keep-out; 4 too near the eye or out of the room.
                        int tier = 4;
                        bool near = GetViewerClearance(target, eye) < clearance;
                        bool room = target.max.y <= FocusMaxHeight + 0.01f &&
                                    target.min.y >= GetFloorTop() + FloorClearance - 0.01f;
                        if (!near && room)
                        {
                            bool seen = TryGetViewRect(target, out Rect rect);
                            float share = seen ? GetShare(rect, free) : 0f;
                            // Over FocusFillLimit of the view's height costs in every tier (a place out of view
                            // can still be a wheel or a door filling the view right beside the eye).
                            float over = seen ? Mathf.Max(0f, rect.height - FocusFillLimit) : 0f;
                            if (IntersectsKeepOut(target))
                            {
                                tier = 3;
                                cost = 100f * (1f - share) + lookCost + 0.1f * cost + 100f * over;
                            }
                            else if (share >= ViewShareRequired)
                            {
                                tier = over > 0f ? 1 : 0;
                                cost += 100f * over;
                            }
                            else
                            {
                                tier = 2;
                                cost = 100f * (1f - share) + lookCost + 0.1f * cost + 100f * over;
                            }
                        }
                        if (tier < best.Tier || (tier == best.Tier && cost < best.Cost))
                            best = new FocusCandidate { Center = target.center, Tier = tier, Cost = cost };
                    }
                }
            }
            // Every rule kept at this turn: no further turn can cost less. Past the side of the view no further turn
            // can bring it into view: the search ends once something outside the car is found.
            if (best.Tier == 0)
                break;
            if (best.Tier <= 2 && FocusTurns[t] > lineToLook + halfView + 10f)
                break;
        }
        foundTier = best.Tier;
        if (best.Tier < 5)
            return best.Center;
        // Nothing outside the car at all (cannot happen round a car on the floor): straight ahead.
        Vector3 ahead = eye + look * preferred;
        ahead.y = GetChestHeight(eye, slope, preferred, lowest, highest);
        return ahead;
    }

    // About chest height `distance` ahead: on the middle line of the free view where it runs between
    // FocusChestBelowEye and BelowEye under the eye, else the nearer of those; within [lowest, highest].
    private static float GetChestHeight(Vector3 eye, float slope, float distance, float lowest, float highest)
    {
        float height = Mathf.Clamp(eye.y + slope * distance, eye.y - FocusChestBelowEye, eye.y - BelowEye);
        return Mathf.Clamp(height, lowest, highest);
    }

    // How far ahead (horizontally) the box fills about FocusFill of the view's height and fits FocusFreeFit of the
    // free view's width and height: measured with its centre 2 m ahead at `height` (its screen box shrinks with the
    // distance), within [nearest, FocusEyeDistanceMax].
    private float GetComfortDistance(Bounds box, Vector3 direction, float height, float nearest, Rect free)
    {
        const float probe = 2f;
        box.center = new Vector3(placementEye.x, height, placementEye.z) + direction * probe;
        float distance = nearest;
        if (TryGetViewRect(box, out Rect rect))
        {
            distance = Mathf.Max(distance, probe * rect.height / FocusFill);
            distance = Mathf.Max(distance, probe * rect.width / Mathf.Max(0.05f, free.width * FocusFreeFit));
            distance = Mathf.Max(distance, probe * rect.height / Mathf.Max(0.05f, free.height * FocusFreeFit));
        }
        else
            distance = Mathf.Max(distance, box.extents.magnitude / Mathf.Max(0.05f, placementTanY * FocusFill));
        return Mathf.Min(distance, FocusEyeDistanceMax);
    }

    // Clear of the viewer (horizontally, unless overhead), under the height limit, off the floor and out of the
    // keep-outs: valid; the score prefers valid places, then room from the viewer.
    private float ScorePlacement(Bounds target, Vector3 viewer, float maxHeight, float viewerClearance,
        out bool valid)
    {
        valid = true;
        if (target.max.y > maxHeight + 0.01f)
            valid = false;
        if (target.min.y < GetFloorTop() + FloorClearance - 0.01f)
            valid = false;
        float clearance = GetViewerClearance(target, viewer);
        if (clearance < viewerClearance)
            valid = false;
        if (IntersectsKeepOut(target))
            valid = false;
        return (valid ? 100f : 0f) + Mathf.Min(clearance, 3f);
    }

    // A place for something the visitor is to look at: valid for ScorePlacement, clear of every other drawn part
    // (GatherPartBoxes) and in view (ViewShareRequired of its screen box in the free part of the view). The score
    // prefers places that are free, then clear of the parts, then more in view, then room from the viewer.
    private float ScoreView(Bounds target, Vector3 viewer, float maxHeight, float viewerClearance, out bool valid)
    {
        float room = ScorePlacement(target, viewer, maxHeight, viewerClearance, out bool placed) - (placed ? 100f : 0f);
        bool clear = !HitsParts(target);
        float share = GetViewShare(target);
        valid = placed && clear && share >= ViewShareRequired;
        return (placed ? 50f : 0f) + (clear ? 25f : 0f) + 40f * share + 0.5f * room;
    }

    // After a level opens or closes, or when a part of it is clicked again, the taken-out assembly stays where it is
    // while it is in view (any part of its box in the free view) and the car does not hide it from the eye; else (the
    // visitor walked away or round the car) it comes to the visitor again by the same rules and the same kind of path
    // (ComputeFocusOffset from where it stands). In one piece or one level apart it also comes again when it is no
    // longer within reach (FocusEyeDistanceMax + FocusRefitSlack from the eye), never to a place more than
    // FocusRefitFurther further (KeepOutOfFootprint then keeps every open level out of the car). True when it moved.
    private bool RefitTakenOut()
    {
        if (takenOut == null || view == CarExplorerView.Exploded || !takenOut.Relocates)
            return false;
        PreparePlacementView();
        Vector3 before = takenOut.FocusOffset;
        float yaw = takenOut.FocusYaw;
        Bounds spread = GetPlannedBounds(takenOut, false);
        Bounds placed = RotateBounds(spread, takenOut.FocusYaw, takenOut.FocusPivot);
        placed.center += takenOut.FocusOffset;
        bool hidden = !SeesPastCar(placed.center) || GetViewShare(placed) <= 0.05f;
        if (hidden)
            takenOut.FocusOffset = ComputeFocusOffset(takenOut, spread, placed.center);
        return takenOut.FocusOffset != before || takenOut.FocusYaw != yaw;
    }

    // Every part of the taken-out assembly, as planned, outside the car's footprint: where an open level reaches
    // into it, the assembly moves away from the car toward the visitor (along the line to the eye, or straight out of
    // the side it is at), as little as it must, while that keeps FocusNearClearance from the eye and the levels in
    // view. Where neither fits between the car and the visitor, the whole assembly with its open levels takes a new place by the same rules as
    // taking it out (ComputeFocusOffset from where it stands: the least turn, in view where it can be); only when that
    // finds no place outside the car does it go out toward the visitor regardless. Out of the car with room to spare,
    // it keeps FocusNearClearance from the eye, backing toward the car no further than the footprint allows.
    private void KeepOutOfFootprint()
    {
        if (takenOut == null || view == CarExplorerView.Exploded || !takenOut.Relocates)
            return;
        PreparePlacementView();
        Vector3 eye = placementEye;
        Bounds whole = GetPlannedBounds(takenOut, true);
        Vector3 toEye = Vector3.ProjectOnPlane(eye - whole.center, Vector3.up);
        Vector3 side = GetFootprintNormal(whole.center);
        Vector3 along = toEye.sqrMagnitude > 0.01f ? toEye.normalized : side;
        float clearance = IsXrView() ? FocusNearClearanceXr : FocusNearClearance;
        float need = GetFootprintExit(takenOut, along);
        if (need <= 0f)
        {
            GetExtentAlong(whole, along, out float center, out float halfAlong);
            float room = Vector3.Dot(eye, along) - (center + halfAlong) - clearance;
            if (room < 0f && GetViewerClearance(whole, eye) < clearance)
            {
                takenOut.FocusOffset += along * Mathf.Max(room, need);
                return;
            }
            // XR: opened near the eye, the assembly with its levels can be bigger than the view (the V6 one level
            // apart, 1.8 m wide, 0.96 m from the eye filled 137 % of its height); it backs toward the car, in
            // FocusBackStep steps up to FocusBackMax and as far as the footprint allows, seen past the car, to the
            // first place where the levels are in view.
            if (IsXrView() && openPath.Count > 0 && GetViewShare(whole) < ViewShareRequired)
            {
                float back = float.IsNegativeInfinity(need) ? FocusBackMax : Mathf.Min(-need, FocusBackMax);
                for (int n = 1; n * FocusBackStep <= back + 0.001f; n++)
                {
                    Bounds moved = whole;
                    moved.center -= along * (n * FocusBackStep);
                    if (!SeesPastCar(moved.center))
                        break;
                    if (GetViewShare(moved) >= ViewShareRequired)
                    {
                        takenOut.FocusOffset -= along * (n * FocusBackStep);
                        break;
                    }
                }
            }
            return;
        }
        float sideNeed = GetFootprintExit(takenOut, side);
        Bounds byLine = whole, bySide = whole;
        bool lineFinite = !float.IsInfinity(need);
        bool sideFinite = sideNeed > 0f && !float.IsInfinity(sideNeed);
        if (lineFinite)
            byLine.center += along * need;
        if (sideFinite)
            bySide.center += side * sideNeed;
        bool lineClear = lineFinite && GetViewerClearance(byLine, eye) >= clearance;
        bool sideClear = sideFinite && GetViewerClearance(bySide, eye) >= clearance;
        float lineShare = lineClear ? GetViewShare(byLine) : 0f;
        float sideShare = sideClear ? GetViewShare(bySide) : 0f;
        bool lineFree = lineClear && lineShare >= ViewShareRequired;
        bool sideFree = sideClear && sideShare >= ViewShareRequired;
        if (lineFree && (!sideFree || need <= 1.5f * sideNeed))
        {
            takenOut.FocusOffset += along * need;
            return;
        }
        if (sideFree)
        {
            takenOut.FocusOffset += side * sideNeed;
            return;
        }
        // Neither push keeps the open levels in the free view (too big so near). A push that keeps the near clearance
        // still stands unless a new place by the take-out rules shows more of them and lies at most FocusKeepTurn
        // round the eye from where the assembly is (a new place far aside or past the car loses the visitor's
        // place in what they were looking at), or the push shows less than FocusKeepShareMin of them.
        Vector3 before = takenOut.FocusOffset;
        float yawBefore = takenOut.FocusYaw;
        Vector3 pivotBefore = takenOut.FocusPivot;
        Vector3 stepBefore = pendingStepBack;
        Vector3 eyeBefore = placementEye;
        bool shiftedBefore = placementShifted;
        bool pushClear = lineClear || sideClear;
        bool pushLine = lineClear && (!sideClear || need <= 1.5f * sideNeed || lineShare >= sideShare);
        Vector3 push = pushLine ? along * need : side * (sideClear ? sideNeed : 0f);
        float pushShare = pushLine ? lineShare : sideShare;
        takenOut.FocusOffset = ComputeFocusOffset(takenOut, GetPlannedBounds(takenOut, false), whole.center);
        float again = GetFootprintExit(takenOut, along);
        bool placed = !(again > 0f && !float.IsInfinity(again));
        if (pushClear)
        {
            bool better = false;
            if (placed)
            {
                if (pendingStepBack != stepBefore)
                    better = true;
                else
                {
                    Bounds now = GetPlannedBounds(takenOut, true);
                    Vector3 from = Vector3.ProjectOnPlane(whole.center - eyeBefore, Vector3.up);
                    Vector3 to = Vector3.ProjectOnPlane(now.center - eyeBefore, Vector3.up);
                    better = GetViewShare(now) > pushShare &&
                             (Vector3.Angle(from, to) <= FocusKeepTurn || pushShare < FocusKeepShareMin);
                }
            }
            if (!better)
            {
                takenOut.FocusYaw = yawBefore;
                takenOut.FocusPivot = pivotBefore;
                pendingStepBack = stepBefore;
                placementEye = eyeBefore;
                placementShifted = shiftedBefore;
                takenOut.FocusOffset = before + push;
            }
            return;
        }
        if (!placed)
        {
            takenOut.FocusYaw = yawBefore;
            takenOut.FocusOffset = before + along * need;
        }
    }

    // How far round the eye (degrees) a new place for an opened assembly may lie from where it is, instead of the
    // push out of the footprint toward the visitor (KeepOutOfFootprint); a push that leaves less than
    // FocusKeepShareMin of the levels' screen box in the free view gives way to any new place that shows more.
    private const float FocusKeepTurn = 15f;
    private const float FocusKeepShareMin = 0.5f;

    // How far the taken-out assembly, as planned, has to move along `direction` (horizontal) until none of its parts'
    // boxes overlaps the footprint; when none does, 0 or less: minus the room it has back toward the car (minus
    // infinity when nothing would run into it).
    private float GetFootprintExit(Node assembly, Vector3 direction)
    {
        float wf = Vector3.Dot(direction, carForward), wr = Vector3.Dot(direction, carRight);
        float need = float.NegativeInfinity, back = float.NegativeInfinity;
        bool any = false;
        foreach (Part part in assembly.AllParts)
        {
            Vector3 move = GetPlannedWorld(part.Leaf);
            foreach (Renderer renderer in part.Renderers)
            {
                if (renderer == null) continue;
                Bounds box = GetRestRendererBounds(part, renderer);
                box.center += move;
                if (assembly == takenOut && assembly.FocusYaw != 0f)
                    box = RotateBounds(box, assembly.FocusYaw, assembly.FocusPivot + GetPlannedOffset(assembly));
                // The interval of moves t along the direction for which this box overlaps the footprint.
                GetFootprintSpan(box, out float bf, out float hf, out float br, out float hr);
                if (!OverlapInterval(bf, hf, footprintCenterF, footprintHalfF, wf, out float lo1, out float hi1) ||
                    !OverlapInterval(br, hr, footprintCenterR, footprintHalfR, wr, out float lo2, out float hi2))
                    continue;
                float lo = Mathf.Max(lo1, lo2), hi = Mathf.Min(hi1, hi2);
                if (lo >= hi)
                    continue;
                // Overlapping where it is: out by the far end of the overlap; else the overlap behind it limits the
                // way back (one ahead of it lies beyond the car, never a way to go).
                if (lo < 0f && hi > 0f)
                {
                    any = true;
                    need = Mathf.Max(need, hi);
                }
                else if (hi <= 0f)
                    back = Mathf.Max(back, hi);
            }
        }
        // The taken-out assembly's whole box (with every open level) counts too: parts each outside the footprint
        // can still reach round a corner of it (the V6 apart: the intake over one side, the rockers beside another).
        if (assembly == takenOut)
        {
            Bounds union = GetPlannedBounds(assembly, true);
            if (assembly.FocusYaw != 0f)
                union = RotateBounds(union, assembly.FocusYaw, assembly.FocusPivot + GetPlannedOffset(assembly));
            GetFootprintSpan(union, out float bf, out float hf, out float br, out float hr);
            if (OverlapInterval(bf, hf, footprintCenterF, footprintHalfF, wf, out float lo1, out float hi1) &&
                OverlapInterval(br, hr, footprintCenterR, footprintHalfR, wr, out float lo2, out float hi2))
            {
                float lo = Mathf.Max(lo1, lo2), hi = Mathf.Min(hi1, hi2);
                if (lo < hi && lo < 0f && hi > 0f)
                {
                    any = true;
                    need = Mathf.Max(need, hi);
                }
                else if (lo < hi && hi <= 0f)
                    back = Mathf.Max(back, hi);
            }
        }
        return any ? need + 0.01f : back;
    }

    // The moves t (along a direction with component w on this axis) for which an interval [b-h, b+h] overlaps the
    // footprint's [c-fh, c+fh]: false when none do; (-inf, inf) when the direction does not change it and it overlaps.
    private static bool OverlapInterval(float b, float h, float c, float fh, float w, out float lo, out float hi)
    {
        float reach = h + fh;
        if (Mathf.Abs(w) < 1e-4f)
        {
            lo = float.NegativeInfinity;
            hi = float.PositiveInfinity;
            return Mathf.Abs(b - c) < reach;
        }
        float a = (c - reach - b) / w, z = (c + reach - b) / w;
        lo = Mathf.Min(a, z);
        hi = Mathf.Max(a, z);
        return true;
    }

    // The cockpit turns its dashboard and wheel (the driver's side) to the viewer, the seats their fronts; the
    // rest keep their orientation, so a door or wheel shows its outer face and spreads across the view. An assembly
    // with a part hung under another assembly's part (TurnBlocked) keeps its heading: that part could not turn with it.
    private float GetFocusYaw(Node assembly, Vector3 pivot, Vector3 viewer)
    {
        Vector3 face;
        if (assembly.TurnBlocked)
            return 0f;
        // Only the cockpit turns (its dashboard toward the viewer); the seats keep their heading: turned on the way,
        // their drawn boxes ran into the car's parts after leaving their place (measured in every X-ray take-out).
        if (assembly.Kind == AssemblyKind.Cockpit)
            face = -carForward;
        else
            return 0f;
        Vector3 toViewer = Vector3.ProjectOnPlane(viewer - pivot, Vector3.up);
        if (toViewer.sqrMagnitude < 0.01f)
            return 0f;
        float yaw = Vector3.SignedAngle(face, toViewer, Vector3.up);
        // Small turns are left out: they only make the assembly look askew.
        return Mathf.Abs(yaw) < 20f ? 0f : yaw;
    }

    // ---------------------------------------------------------------- the car's footprint, its top and the paths

    // The car's rest bounds in its own horizontal axes (relative to its rest centre), seen from above and grown by
    // FootprintMargin: what a taken-out assembly ends outside of. Its box (not grown) is what must not stand between
    // the eye and the assembly.
    private void MeasureFootprint()
    {
        float fMin = float.PositiveInfinity, fMax = float.NegativeInfinity;
        float rMin = float.PositiveInfinity, rMax = float.NegativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = GetBoxCorner(carRestBounds, i) - carRestCenter;
            float f = Vector3.Dot(corner, carForward), r = Vector3.Dot(corner, carRight);
            fMin = Mathf.Min(fMin, f); fMax = Mathf.Max(fMax, f);
            rMin = Mathf.Min(rMin, r); rMax = Mathf.Max(rMax, r);
        }
        bodyCenterF = 0.5f * (fMin + fMax);
        bodyHalfF = 0.5f * (fMax - fMin);
        bodyCenterR = 0.5f * (rMin + rMax);
        bodyHalfR = 0.5f * (rMax - rMin);
        footprintCenterF = bodyCenterF;
        footprintCenterR = bodyCenterR;
        footprintHalfF = bodyHalfF + FootprintMargin;
        footprintHalfR = bodyHalfR + FootprintMargin;
    }

    // A box's centre and half size along the car's forward and right axes (relative to its rest centre).
    private void GetFootprintSpan(Bounds box, out float centerF, out float halfF, out float centerR, out float halfR)
    {
        Vector3 local = box.center - carRestCenter;
        Vector3 e = box.extents;
        centerF = Vector3.Dot(local, carForward);
        halfF = Mathf.Abs(e.x * carForward.x) + Mathf.Abs(e.z * carForward.z);
        centerR = Vector3.Dot(local, carRight);
        halfR = Mathf.Abs(e.x * carRight.x) + Mathf.Abs(e.z * carRight.z);
    }

    // Seen from above, the box lies outside the car's footprint (grown by FootprintMargin, plus `extra`).
    private bool IsOutsideFootprint(Bounds box, float extra = 0f)
    {
        GetFootprintSpan(box, out float bf, out float hf, out float br, out float hr);
        return Mathf.Abs(bf - footprintCenterF) >= hf + footprintHalfF + extra ||
               Mathf.Abs(br - footprintCenterR) >= hr + footprintHalfR + extra;
    }

    // The car's box (not grown) does not stand between the eye and the point.
    private bool SeesPastCar(Vector3 point)
    {
        Vector3 a = placementEye - carRestCenter, b = point - carRestCenter;
        float tMin = 0f, tMax = 1f;
        return ClipSlab(Vector3.Dot(a, carForward), Vector3.Dot(b, carForward), bodyCenterF - bodyHalfF,
                   bodyCenterF + bodyHalfF, ref tMin, ref tMax) == false ||
               ClipSlab(Vector3.Dot(a, carRight), Vector3.Dot(b, carRight), bodyCenterR - bodyHalfR,
                   bodyCenterR + bodyHalfR, ref tMin, ref tMax) == false ||
               ClipSlab(placementEye.y, point.y, carRestBounds.min.y, carRestBounds.max.y, ref tMin, ref tMax) == false;
    }

    // Narrows [tMin, tMax] of the segment a..b to the slab [min, max] on one axis; false when nothing is left.
    private static bool ClipSlab(float a, float b, float min, float max, ref float tMin, ref float tMax)
    {
        float d = b - a;
        if (Mathf.Abs(d) < 1e-6f)
            return a > min && a < max;
        float t1 = (min - a) / d, t2 = (max - a) / d;
        if (t1 > t2) { float s = t1; t1 = t2; t2 = s; }
        tMin = Mathf.Max(tMin, t1);
        tMax = Mathf.Min(tMax, t2);
        return tMin < tMax;
    }

    // The outward normal (horizontal) of the footprint's side the point lies furthest beyond.
    private Vector3 GetFootprintNormal(Vector3 point)
    {
        Vector3 local = point - carRestCenter;
        float f = (Vector3.Dot(local, carForward) - footprintCenterF) / Mathf.Max(0.01f, footprintHalfF);
        float r = (Vector3.Dot(local, carRight) - footprintCenterR) / Mathf.Max(0.01f, footprintHalfR);
        return Mathf.Abs(f) > Mathf.Abs(r) ? carForward * Mathf.Sign(f) : carRight * (r >= 0f ? 1f : -1f);
    }

    // The top of the car (its own parts, not the content inside) on a grid seen from above, found once from the exact
    // picking meshes at their rest poses (a ray down per BodyCell cell). Until the picking meshes are ready the top of
    // the car's box stands for it.
    private void EnsureBodyHeights()
    {
        if (bodyHeights != null || !pickReady || !pickPhysics.IsValid())
            return;
        foreach (Part part in pickParts)
        {
            Transform picker = part.PickCollider != null ? part.PickCollider.transform : null;
            if (picker == null)
                continue;
            if (trackByTransform.TryGetValue(part.Target, out MotionTrack track))
                picker.SetPositionAndRotation(track.RestWorldPosition,
                    track.ParentRestRotation * track.RestLocalRotation);
            else
                picker.SetPositionAndRotation(part.Target.position, part.Target.rotation);
            picker.localScale = part.Target.lossyScale;
        }
        Physics.SyncTransforms();
        float pad = FootprintMargin + 2f * BodyCell;
        bodyCellsF = Mathf.Max(1, Mathf.CeilToInt(2f * (bodyHalfF + pad) / BodyCell));
        bodyCellsR = Mathf.Max(1, Mathf.CeilToInt(2f * (bodyHalfR + pad) / BodyCell));
        bodyCellOriginF = bodyCenterF - bodyHalfF - pad;
        bodyCellOriginR = bodyCenterR - bodyHalfR - pad;
        bodyHeights = new float[bodyCellsF * bodyCellsR];
        float top = carRestBounds.max.y + 0.5f;
        float depth = top - carRestBounds.min.y + 0.5f;
        for (int i = 0; i < bodyCellsF; i++)
            for (int j = 0; j < bodyCellsR; j++)
            {
                Vector3 origin = carRestCenter + carForward * (bodyCellOriginF + (i + 0.5f) * BodyCell) +
                                 carRight * (bodyCellOriginR + (j + 0.5f) * BodyCell);
                origin.y = top;
                int count = pickPhysics.Raycast(origin, Vector3.down, heightHits, depth,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float height = float.NegativeInfinity;
                for (int k = 0; k < count; k++)
                    if (partByPickCollider.TryGetValue(heightHits[k].collider, out Part hit) && !hit.IsInner)
                        height = Mathf.Max(height, heightHits[k].point.y);
                // A full buffer may have left the top out: the box's top stands for it.
                bodyHeights[i * bodyCellsR + j] = count >= heightHits.Length ? carRestBounds.max.y : height;
            }
    }

    // The highest point of the car's top under the box seen from above (a cell's neighbours count, for thin parts
    // between the rays); minus infinity where the box is off the car.
    private float GetBodyTop(Bounds box)
    {
        GetFootprintSpan(box, out float bf, out float hf, out float br, out float hr);
        if (Mathf.Abs(bf - bodyCenterF) >= hf + bodyHalfF || Mathf.Abs(br - bodyCenterR) >= hr + bodyHalfR)
            return float.NegativeInfinity;
        if (bodyHeights == null)
            return carRestBounds.max.y;
        int i0 = Mathf.Max(0, Mathf.FloorToInt((bf - hf - bodyCellOriginF) / BodyCell) - 1);
        int i1 = Mathf.Min(bodyCellsF - 1, Mathf.FloorToInt((bf + hf - bodyCellOriginF) / BodyCell) + 1);
        int j0 = Mathf.Max(0, Mathf.FloorToInt((br - hr - bodyCellOriginR) / BodyCell) - 1);
        int j1 = Mathf.Min(bodyCellsR - 1, Mathf.FloorToInt((br + hr - bodyCellOriginR) / BodyCell) + 1);
        float height = float.NegativeInfinity;
        for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
                height = Mathf.Max(height, bodyHeights[i * bodyCellsR + j]);
        return height;
    }

    // The highest point of the car's top under the box as it moves (horizontally) from a to b.
    private float GetBodyTopAlong(Bounds box, Vector3 a, Vector3 b)
    {
        float length = HorizontalDistance(a, b);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / (0.5f * BodyCell)));
        float height = float.NegativeInfinity;
        for (int s = 0; s <= steps; s++)
        {
            box.center = Vector3.Lerp(a, b, s / (float)steps);
            height = Mathf.Max(height, GetBodyTop(box));
        }
        return height;
    }

    // Eyes checked along a desktop step back's glide (from where the visitor stood to where it ends), so a path that
    // crosses the way they glide goes over their head: the walking body's capsule is pushed or lifted by a part that
    // runs into it.
    private const int GlideEyeSamples = 6;

    // PassesVisitor for the placement eye, and while a step back is planned for every eye along its glide.
    private bool PassesGlidingVisitor(Bounds box, Vector3 a, Vector3 b)
    {
        if (PassesVisitor(box, a, b, placementEye))
            return true;
        if (!placementShifted)
            return false;
        for (int e = 1; e <= GlideEyeSamples; e++)
            if (PassesVisitor(box, a, b, Vector3.Lerp(placementEye, placementBodyEye, e / (float)GlideEyeSamples)))
                return true;
        return false;
    }

    // Whether the box moving in a straight line from a to b comes within PathViewerClearance (horizontally) of the eye
    // below the head (GetViewerClearance counts a box over the head as clear).
    private static bool PassesVisitor(Bounds box, Vector3 a, Vector3 b, Vector3 eye)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / (0.5f * BodyCell)));
        for (int s = 0; s <= steps; s++)
        {
            box.center = Vector3.Lerp(a, b, s / (float)steps);
            if (GetViewerClearance(box, eye) < PathViewerClearance)
                return true;
        }
        return false;
    }

    // Whether the box moving in a straight line from a to b stays above the car's top wherever it is over the car.
    private bool IsClearAlong(Bounds box, Vector3 a, Vector3 b)
    {
        float length = Vector3.Distance(a, b);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / (0.5f * BodyCell)));
        for (int s = 0; s <= steps; s++)
        {
            box.center = Vector3.Lerp(a, b, s / (float)steps);
            if (box.min.y < GetBodyTop(box) + 0.5f * PathClearance)
                return false;
        }
        return true;
    }

    // The box an assembly travels as: closed (its levels open after it arrives and close before it leaves), with its
    // levels as they are and as planned where they stay open, and, when it turns on the way, every heading it passes.
    private Bounds GetTravelBox(Node assembly)
    {
        Bounds box = assembly.RestBounds;
        foreach (Part part in assembly.AllParts)
        {
            Vector3 now = part.Leaf.World - assembly.World;
            Vector3 planned = GetPlannedWorld(part.Leaf) - GetPlannedOffset(assembly);
            foreach (Renderer renderer in part.Renderers)
            {
                if (renderer == null) continue;
                Bounds bounds = GetRestRendererBounds(part, renderer);
                Bounds moved = bounds;
                moved.center += now;
                box.Encapsulate(moved);
                moved.center = bounds.center + planned;
                box.Encapsulate(moved);
            }
        }
        if (assembly.FromYaw != 0f || assembly.TargetYaw != 0f)
        {
            Vector3 pivot = assembly.FocusPivot;
            float radius = 0f;
            for (int i = 0; i < 8; i++)
                radius = Mathf.Max(radius, HorizontalDistance(GetBoxCorner(box, i), pivot));
            box = new Bounds(new Vector3(pivot.x, box.center.y, pivot.z),
                new Vector3(2f * radius, box.size.y, 2f * radius));
        }
        return box;
    }

    // The way a taken-out assembly goes (outside the exploded view), as offsets from its rest place: never straight
    // through the car. Out of its place first (straight up until its bottom clears the car's top over it, or, for a
    // door or a wheel, straight out of the side until it is clear of the footprint), then at a height where it clears
    // the car's top (PathClearance) everywhere on the way, over to its new place, and down (or up) onto it. Put back
    // runs the way out in reverse; a move between two places outside the car goes straight when nothing is in the
    // way, else up, over and down. The corners are rounded (PathCornerRadius) and the speed eases in and out.
    // The path is checked as it will be run (PathCheckSamples points along its eased length): where the travelling box
    // runs into another part after it first cleared its place, the way over the car is raised by PathRaiseStep and the
    // path built again, at most PathRaiseTries times.
    private void BuildPath(Node assembly)
    {
        bool overHead = false;
        for (int attempt = 0; attempt <= PathRaiseTries; attempt++)
        {
            BuildPath(assembly, attempt * PathRaiseStep, overHead);
            if (assembly.PathCount == 0)
                return;
            bool nearVisitor = PathPassesVisitor(assembly);
            if (!nearVisitor && !PathHitsAfterLeaving(assembly))
                return;
            overHead |= nearVisitor;
        }
    }

    // Whether the path as it will run (PathCheckSamples points along its eased length, corners rounded) brings the
    // travelling box nearer than PathViewerClearance to the visitor's eye below their head (while a step back is
    // planned, any eye on the glide from where they stood to where it ends), nearer than both of its ends are: an end
    // place itself keeps the near clearance.
    private bool PathPassesVisitor(Node assembly)
    {
        PreparePlacementView();
        Bounds box = GetTravelBox(assembly);
        Vector3 rest = box.center;
        bool home = assembly.Target == Vector3.zero;
        for (int eyeIndex = 0; eyeIndex <= (placementShifted ? GlideEyeSamples : 0); eyeIndex++)
        {
            Vector3 eye = Vector3.Lerp(placementEye, placementBodyEye, eyeIndex / (float)GlideEyeSamples);
            box.center = rest + assembly.Path[0];
            float limit = GetViewerClearance(box, eye);
            box.center = rest + assembly.Path[assembly.PathCount - 1];
            limit = Mathf.Min(PathViewerClearance, limit, GetViewerClearance(box, eye)) - 0.02f;
            for (int k = 1; k < PathCheckSamples; k++)
            {
                float amount = k / (float)PathCheckSamples;
                box.center = rest + EvaluatePath(assembly, home ? 1f - amount : amount);
                if (GetViewerClearance(box, eye) < limit)
                    return true;
            }
        }
        return false;
    }

    private bool PathHitsAfterLeaving(Node assembly)
    {
        Bounds box = GetTravelBox(assembly);
        Vector3 rest = box.center;
        bool home = assembly.Target == Vector3.zero;
        GatherPartBoxes(assembly);
        bool cleared = false;
        for (int k = 0; k <= PathCheckSamples; k++)
        {
            float amount = k / (float)PathCheckSamples;
            box.center = rest + EvaluatePath(assembly, home ? 1f - amount : amount);
            bool hit = HitsParts(box);
            if (!hit)
                cleared = true;
            else if (cleared)
                return true;
        }
        return false;
    }

    private void BuildPath(Node assembly, float raise, bool overHead)
    {
        assembly.PathCount = 0;
        if (view == CarExplorerView.Exploded || !assembly.Moving || assembly.From == assembly.Target)
            return;
        Bounds box = GetTravelBox(assembly);
        Vector3 rest = box.center;
        bool home = assembly.Target == Vector3.zero;
        Vector3 start = rest + (home ? assembly.Target : assembly.From);
        Vector3 end = rest + (home ? assembly.From : assembly.Target);
        pathScratch.Clear();
        pathScratch.Add(start);
        Vector3 at = start;
        box.center = start;
        // Leaving its own place (the way out from rest, or the way home run backwards): a door or a wheel goes
        // straight out of the side, unless that would bring it to the visitor standing there; then it lifts out.
        bool atRest = (home ? assembly.Target : assembly.From) == Vector3.zero;
        bool inCar = !IsOutsideFootprint(box) && box.min.y < GetBodyTop(box) + PathClearance;
        if (!overHead && atRest && inCar && (assembly.Kind == AssemblyKind.Door || assembly.Kind == AssemblyKind.Wheel))
        {
            Vector3 outward = carRight * (assembly.Side != 0f ? assembly.Side : 1f);
            GetFootprintSpan(box, out float bf, out float hf, out float br, out float hr);
            float edge = outward == carRight ? footprintCenterR + footprintHalfR : footprintCenterR - footprintHalfR;
            float travel = Mathf.Max(0f, Vector3.Dot(outward, carRight) * (edge - br) + hr + 0.02f);
            Bounds outside = box;
            outside.center = start + outward * travel;
            PreparePlacementView();
            // The whole sideways leg (run either way) keeps clear of the visitor, not only its end.
            if (!PassesGlidingVisitor(box, start, outside.center))
            {
                at = outside.center;
                pathScratch.Add(at);
            }
        }
        box.center = at;
        PreparePlacementView();
        bool pastVisitor = PassesGlidingVisitor(box, at, end);
        if (pastVisitor || overHead || raise > 0f || !IsClearAlong(box, at, end))
        {
            // Up to a height where its bottom clears the car's top everywhere under its way (and the visitor's head
            // where it passes them), then over and down.
            float top = Mathf.Max(GetBodyTop(box), GetBodyTopAlong(box, at, end));
            float over = top + PathClearance + raise + box.extents.y;
            Bounds high = box;
            high.center = new Vector3(at.x, over, at.z);
            if (overHead || PassesGlidingVisitor(high, high.center, new Vector3(end.x, over, end.z)))
                over = Mathf.Max(over, placementEye.y + BelowEye + PathClearance + raise + box.extents.y);
            if (at.y < over - 0.005f)
                pathScratch.Add(new Vector3(at.x, over, at.z));
            if (end.y < over - 0.005f)
                pathScratch.Add(new Vector3(end.x, Mathf.Max(over, at.y), end.z));
        }
        pathScratch.Add(end);
        if (home)
            pathScratch.Reverse();
        int count = 0;
        if (assembly.Path == null || assembly.Path.Length < pathScratch.Count)
            assembly.Path = new Vector3[Mathf.Max(6, pathScratch.Count)];
        foreach (Vector3 point in pathScratch)
        {
            Vector3 offset = point - rest;
            if (count > 0 && (offset - assembly.Path[count - 1]).sqrMagnitude < 1e-6f)
                continue;
            assembly.Path[count++] = offset;
        }
        // Exactly the move's ends.
        assembly.Path[0] = assembly.From;
        assembly.Path[count - 1] = assembly.Target;
        if (count < 3)
            return;
        assembly.PathCount = count;
        assembly.PathLength = 0f;
        for (int i = 1; i < count; i++)
            assembly.PathLength += Vector3.Distance(assembly.Path[i - 1], assembly.Path[i]);
        // It turns (the cockpit, the seats) once it is out of its place and before it settles.
        float first = Vector3.Distance(assembly.Path[0], assembly.Path[1]);
        float last = Vector3.Distance(assembly.Path[count - 2], assembly.Path[count - 1]);
        assembly.PathTurnStart = Mathf.Clamp01(first / Mathf.Max(1e-4f, assembly.PathLength));
        assembly.PathTurnEnd = Mathf.Clamp01(1f - last / Mathf.Max(1e-4f, assembly.PathLength));
        if (assembly.PathTurnEnd <= assembly.PathTurnStart + 0.05f)
        {
            assembly.PathTurnStart = 0f;
            assembly.PathTurnEnd = 1f;
        }
    }

    // The point `amount` (0..1, eased) along an assembly's path, at even speed along its length, with each corner
    // rounded into a curve tangent to both sides (a quadratic through the corner, PathCornerRadius each way).
    private static Vector3 EvaluatePath(Node node, float amount)
    {
        Vector3[] path = node.Path;
        int count = node.PathCount;
        float s = Mathf.Clamp01(amount) * node.PathLength;
        float start = 0f;
        for (int i = 0; i < count - 1; i++)
        {
            float length = Vector3.Distance(path[i], path[i + 1]);
            float end = start + length;
            if (s <= end || i == count - 2)
            {
                float along = Mathf.Clamp(s - start, 0f, length);
                // Near a corner (not the path's ends) the curve takes over.
                if (i + 1 < count - 1)
                {
                    float next = Vector3.Distance(path[i + 1], path[i + 2]);
                    float r = Mathf.Min(PathCornerRadius, 0.45f * length, 0.45f * next);
                    if (r > 1e-4f && along > length - r)
                        return RoundCorner(path[i], path[i + 1], path[i + 2], r, along - (length - r));
                }
                if (i > 0)
                {
                    float previous = Vector3.Distance(path[i - 1], path[i]);
                    float r = Mathf.Min(PathCornerRadius, 0.45f * length, 0.45f * previous);
                    if (r > 1e-4f && along < r)
                        return RoundCorner(path[i - 1], path[i], path[i + 1], r, r + along);
                }
                return length > 1e-6f ? Vector3.Lerp(path[i], path[i + 1], along / length) : path[i + 1];
            }
            start = end;
        }
        return path[count - 1];
    }

    // On the rounded corner at b (from the side a-b into b-c): `into` (0..2r) along it.
    private static Vector3 RoundCorner(Vector3 a, Vector3 b, Vector3 c, float r, float into)
    {
        Vector3 p0 = b + (a - b).normalized * r;
        Vector3 p2 = b + (c - b).normalized * r;
        float t = Mathf.Clamp01(into / (2f * r));
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * b + t * t * p2;
    }

    // The share of the path's length after which an assembly turning on the way (the cockpit, the seats) has turned.
    private static float GetPathTurn(Node node, float amount)
    {
        if (node.PathCount < 3)
            return amount;
        float t = (amount - node.PathTurnStart) / Mathf.Max(1e-4f, node.PathTurnEnd - node.PathTurnStart);
        return Smooth(Mathf.Clamp01(t));
    }

    private static Bounds RotateBounds(Bounds bounds, float yaw, Vector3 pivot)
    {
        if (yaw == 0f)
            return bounds;
        Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
        Bounds result = new Bounds(pivot + turn * (GetBoxCorner(bounds, 0) - pivot), Vector3.zero);
        for (int i = 1; i < 8; i++)
            result.Encapsulate(pivot + turn * (GetBoxCorner(bounds, i) - pivot));
        return result;
    }

    private static void GetExtentAlong(Bounds bounds, Vector3 axis, out float center, out float half)
    {
        center = Vector3.Dot(bounds.center, axis);
        Vector3 e = bounds.extents;
        half = Mathf.Abs(e.x * axis.x) + Mathf.Abs(e.y * axis.y) + Mathf.Abs(e.z * axis.z);
    }

    private float GetFloorTop()
    {
        return Mathf.Max(showroomFloorHeight, carRoot.position.y);
    }

    // Horizontal distance between the viewer's eye and the box, or more if the box floats over their head.
    private static float GetViewerClearance(Bounds box, Vector3 eye)
    {
        if (box.min.y > eye.y + 0.25f)
            return 10f;
        float dx = Mathf.Max(0f, Mathf.Max(box.min.x - eye.x, eye.x - box.max.x));
        float dz = Mathf.Max(0f, Mathf.Max(box.min.z - eye.z, eye.z - box.max.z));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // The planned boxes of the drawn parts outside `skip` (the node being placed): what it must not run into. Inner
    // content counts where the planned state draws it. The exact picking colliders stand at the planned places too
    // (ProcessRay puts them back where the parts are before every ray).
    private void GatherPartBoxes(Node skip)
    {
        partBoxes.Clear();
        partBoxOwners.Clear();
        placementStamp++;
        foreach (Part part in parts)
        {
            if (part.Leaf == null || Contains(skip, part))
                continue;
            if (part.IsInner && part.Assembly != null && part.Assembly.Inner && !IsInnerNeededAtRest(part.Assembly))
                continue;
            Vector3 move = GetPlannedWorld(part.Leaf);
            bool drawn = false;
            foreach (Renderer renderer in part.Renderers)
            {
                if (renderer == null || (!renderer.enabled && !part.IsInner))
                    continue;
                Bounds bounds = GetRestRendererBounds(part, renderer);
                bounds.center += move;
                partBoxes.Add(bounds);
                partBoxOwners.Add(part);
                drawn = true;
            }
            if (drawn)
                part.PlacementStamp = placementStamp;
        }
        if (!pickReady)
            return;
        foreach (Part part in pickParts)
        {
            Transform picker = part.PickCollider != null ? part.PickCollider.transform : null;
            if (picker == null)
                continue;
            Transform source = part.Target;
            Vector3 position = trackByTransform.TryGetValue(source, out MotionTrack track) && part.Leaf != null
                ? track.RestWorldPosition + GetPlannedWorld(part.Leaf) : source.position;
            picker.SetPositionAndRotation(position, source.rotation);
            picker.localScale = source.lossyScale;
        }
        Physics.SyncTransforms();
    }

    // True when the box (grown by the sibling gap) runs into one of the parts GatherPartBoxes gathered. The boxes are
    // coarse (a body panel's box spans the car), so where a part has an exact collider, its mesh decides.
    private bool HitsParts(Bounds box)
    {
        box.Expand(2f * SiblingGap);
        bool exactNeeded = false;
        for (int i = 0; i < partBoxes.Count; i++)
        {
            if (!partBoxes[i].Intersects(box))
                continue;
            if (!pickReady || partBoxOwners[i].PickCollider == null)
                return true;
            exactNeeded = true;
        }
        if (!exactNeeded || !pickPhysics.IsValid())
            return false;
        int count = pickPhysics.OverlapBox(box.center, box.extents, overlapBuffer, Quaternion.identity,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (partByPickCollider.TryGetValue(overlapBuffer[i], out Part part) && part.PlacementStamp == placementStamp)
                return true;
        // A full buffer may have left a hit out: count it as one.
        return count >= overlapBuffer.Length;
    }

    // The free part of the view: what the HUD leaves free on the desktop, the central 70 % of the field of view in XR.
    private Rect GetFreeView()
    {
        if (IsXrView())
            return XrViewArea;
        Rect free = CarExplorerModels.HudSafeViewport;
        return free.width > 0.05f && free.height > 0.05f ? free : new Rect(0f, 0f, 1f, 1f);
    }

    // The share of the box's screen rectangle that lies in the free part of the view (0 when it reaches behind the
    // eye; 1 without a camera).
    private float GetViewShare(Bounds box)
    {
        if (viewCamera == null)
            return 1f;
        if (!TryGetViewRect(box, out Rect rect))
            return 0f;
        return GetShare(rect, GetFreeView());
    }

    private static float GetShare(Rect rect, Rect free)
    {
        float x = Mathf.Max(0f, Mathf.Min(rect.xMax, free.xMax) - Mathf.Max(rect.xMin, free.xMin));
        float y = Mathf.Max(0f, Mathf.Min(rect.yMax, free.yMax) - Mathf.Max(rect.yMin, free.yMin));
        float area = rect.width * rect.height;
        return area > 1e-8f ? x * y / area : free.Contains(rect.center) ? 1f : 0f;
    }

    // The box's screen rectangle in the view placement works for (PreparePlacementView); false when it reaches
    // behind the near plane.
    private bool TryGetViewRect(Bounds box, out Rect rect)
    {
        rect = default;
        bool manual = placementFromWalk || placementShifted || viewCamera == null;
        float near = manual ? placementNear : viewCamera.nearClipPlane;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = GetBoxCorner(box, i);
            Vector3 point = manual ? GetPlacementViewportPoint(corner) : viewCamera.WorldToViewportPoint(corner);
            if (point.z < near)
                return false;
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    // ---------------------------------------------------------------- the view placement works for

    // Placement looks through the camera as it is when the command runs, except while the desktop inspect camera is
    // away from the body (and flies back to it): then it looks through the walking view the inspect camera left, where
    // the visitor will look from again, so nothing opened while inspecting lands at the edge of that view.
    private void PreparePlacementView()
    {
        placementFromWalk = walkViewKnown && walkCamera == viewCamera && viewCamera != null && !IsXrView() &&
                            (CarExplorerInspect.Active || inspectReturnPending);
        if (placementFromWalk)
        {
            placementEye = walkEye;
            placementRotation = walkRotation;
            placementTanX = walkTanX;
            placementTanY = walkTanY;
            placementNear = walkNear;
        }
        else if (viewCamera != null)
        {
            Transform cameraTransform = viewCamera.transform;
            placementEye = cameraTransform.position;
            placementRotation = cameraTransform.rotation;
            placementTanY = Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            placementTanX = placementTanY * Mathf.Max(0.1f, viewCamera.aspect);
            placementNear = viewCamera.nearClipPlane;
        }
        else
        {
            // No camera: a visitor 6 m in front of the car, looking at it.
            placementEye = carRestCenter + carForward * 6f + Vector3.up * 1.1f;
            placementRotation = Quaternion.LookRotation(carRestCenter - placementEye, Vector3.up);
            placementTanY = Mathf.Tan(30f * Mathf.Deg2Rad);
            placementTanX = placementTanY * 16f / 9f;
            placementNear = 0.05f;
        }
        placementInverse = Quaternion.Inverse(placementRotation);
        // A step back planned in this command or under way: placement works for the eye at its end.
        placementBodyEye = placementEye;
        placementShifted = false;
        if (!placementFromWalk && viewCamera != null && !IsXrView())
        {
            ICarExplorerStepBackBody stepBody = CarExplorerStepBack.Body;
            Vector3 shift = pendingStepBack + (stepBody != null ? stepBody.StepBackRemaining : Vector3.zero);
            if (shift.sqrMagnitude > 1e-6f)
            {
                placementEye += shift;
                placementShifted = true;
            }
        }
    }

    // A world point in the placement view's viewport (z: the distance ahead of the eye), for a plain perspective view.
    private Vector3 GetPlacementViewportPoint(Vector3 world)
    {
        Vector3 local = placementInverse * (world - placementEye);
        if (local.z <= 1e-4f)
            return new Vector3(0.5f, 0.5f, local.z);
        return new Vector3(0.5f + 0.5f * local.x / (local.z * placementTanX),
            0.5f + 0.5f * local.y / (local.z * placementTanY), local.z);
    }

    // The direction from the placement eye through a viewport point.
    private Vector3 GetPlacementDirection(Vector2 viewportPoint)
    {
        Vector3 local = new Vector3((viewportPoint.x * 2f - 1f) * placementTanX, (viewportPoint.y * 2f - 1f) * placementTanY, 1f);
        return (placementRotation * local).normalized;
    }

    // Every frame: the walking view is recorded while the desktop camera follows the body. When the inspect camera
    // ends, its flight back to the body is awaited (the camera holds still on the body for two frames, or
    // InspectReturnTimeout); then what is out is placed again for the walking eye where it no longer fits.
    private void TrackWalkingView()
    {
        bool inspecting = CarExplorerInspect.Active && !IsXrView();
        if (inspecting != inspectSeen)
        {
            inspectSeen = inspecting;
            inspectReturnPending = !inspecting && walkViewKnown;
            inspectReturnClock = 0f;
            inspectStillFrames = 0;
            if (viewCamera != null)
                inspectReturnLocal = viewCamera.transform.localPosition;
        }
        if (viewCamera == null || inspecting)
            return;
        if (inspectReturnPending)
        {
            inspectReturnClock += Time.unscaledDeltaTime;
            Vector3 local = viewCamera.transform.localPosition;
            inspectStillFrames = (local - inspectReturnLocal).sqrMagnitude < 1e-10f ? inspectStillFrames + 1 : 0;
            inspectReturnLocal = local;
            if ((inspectStillFrames < 2 || inspectReturnClock < 0.15f) && inspectReturnClock < InspectReturnTimeout)
                return;
            inspectReturnPending = false;
            RecordWalkingView();
            ReplaceForWalkingEye();
            return;
        }
        RecordWalkingView();
    }

    private void RecordWalkingView()
    {
        Transform cameraTransform = viewCamera.transform;
        walkCamera = viewCamera;
        walkEye = cameraTransform.position;
        walkRotation = cameraTransform.rotation;
        walkTanY = Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        walkTanX = walkTanY * Mathf.Max(0.1f, viewCamera.aspect);
        walkNear = viewCamera.nearClipPlane;
        walkViewKnown = true;
    }

    // Back from the inspect camera at the walking eye: the taken-out assembly (in one piece or one level apart) comes
    // to the visitor again where it no longer fits that view (RefitTakenOut), and the open levels come out again from
    // there, or when the deepest one is out of the free view; nothing moves while everything still fits.
    private void ReplaceForWalkingEye()
    {
        if (!initialized || takenOut == null || view == CarExplorerView.Exploded)
            return;
        bool moved = RefitTakenOut();
        bool pull = moved;
        if (!pull && openPath.Count > 0 && openPath[openPath.Count - 1] != takenOut)
        {
            PreparePlacementView();
            pull = GetViewShare(GetPlannedBounds(openPath[openPath.Count - 1], true)) < ViewShareRequired;
        }
        if (!pull)
            return;
        foreach (Node node in openPath)
            ComputePull(node);
        foreach (Node node in openPath)
            KeepSpreadInRoom(node);
        KeepOutOfFootprint();
        StartStateMove(false);
        NotifyChanged();
    }

    // The podium under the car (Circular Display), seen from above: its centre and radius, when it is under the car.
    private void FindPodium()
    {
        hasPodium = false;
        GameObject podium = GameObject.Find("Circular Display");
        if (podium == null || !TryGetRendererBounds(podium.transform, out Bounds bounds))
            return;
        podiumCenter = new Vector2(bounds.center.x, bounds.center.z);
        podiumRadius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        hasPodium = podiumRadius > PodiumInset + 1f && podiumRadius < 10f &&
                    Vector2.Distance(podiumCenter, new Vector2(carRestCenter.x, carRestCenter.z)) < podiumRadius;
    }

    // Seen from above, the box lies PodiumInset inside the podium's edge (always true without a podium).
    private bool IsOnPodium(Bounds box)
    {
        if (!hasPodium)
            return true;
        float limit = (podiumRadius - PodiumInset) * (podiumRadius - PodiumInset);
        for (int i = 0; i < 4; i++)
        {
            float x = ((i & 1) == 0 ? box.min.x : box.max.x) - podiumCenter.x;
            float z = ((i & 2) == 0 ? box.min.z : box.max.z) - podiumCenter.y;
            if (x * x + z * z > limit)
                return false;
        }
        return true;
    }

    private void GatherFocusKeepOuts()
    {
        GatherKeepOuts();
        if (presenterRoot != null && presenterRoot.TryGetComponent(out CharacterController presenterBody) &&
            presenterBody.enabled)
            keepOuts.Add(presenterBody.bounds);
    }

    // Colliders around the car that nothing may move into: the other display cars, walls, furniture, people.
    // The floor and display (low and flat), the car itself and the viewer's rig are skipped.
    private void GatherKeepOuts()
    {
        keepOuts.Clear();
        Vector3 center = carRestCenter + Vector3.up * 1.5f;
        Vector3 halfExtents = new Vector3(7f, 2.5f, 7f);
        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, keepOutBuffer, Quaternion.identity,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Transform viewerRoot = viewCamera != null ? viewCamera.transform.root : null;
        for (int i = 0; i < count; i++)
        {
            Collider collider = keepOutBuffer[i];
            if (collider == null || collider is CharacterController)
                continue;
            Transform node = collider.transform;
            if (node.IsChildOf(carRoot) || (viewerRoot != null && node.IsChildOf(viewerRoot)))
                continue;
            if (presenterRoot != null && node.IsChildOf(presenterRoot))
                continue;
            Bounds bounds = collider.bounds;
            // The floor, the display plinth and other flat ground pieces.
            if (bounds.max.y < GetFloorTop() + 0.12f || bounds.size.y < 0.02f)
                continue;
            // Huge enclosing shells (the hall's walls as one mesh) would block everything; their faces are far.
            if (bounds.size.x > 25f || bounds.size.z > 25f)
                continue;
            keepOuts.Add(bounds);
        }
        // The display cars get their colliders only with the desktop walkthrough; their meshes count either way.
        if (premiumCollection == null)
            premiumCollection = GameObject.Find("Showroom Premium Collection");
        if (premiumCollection != null)
            foreach (Transform display in premiumCollection.transform)
                if (TryGetRendererBounds(display, out Bounds displayBounds))
                    keepOuts.Add(displayBounds);
    }

    private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    private bool IntersectsKeepOut(Bounds box)
    {
        box.Expand(0.1f);
        foreach (Bounds keepOut in keepOuts)
            if (keepOut.Intersects(box))
                return true;
        return false;
    }

    // ---------------------------------------------------------------- exploded view

    // Exploded view of the whole car (as in a workshop manual), every assembly as one unit. CarConcept's own
    // assemblies keep the tuned three tiers: the floor tub with the wheels out along their axles and the V6
    // forward; the seats and cockpit lifted out of the tub; the body frame above them with the roof and windscreen
    // on top, the doors beside it, the front section over its nose and the rear section behind it.
    private Vector3 GetExplodeOffset(Node assembly)
    {
        Vector3 up = Vector3.up;
        Vector3 outward = carRight * assembly.Side;
        switch (assembly.Kind)
        {
            case AssemblyKind.BodyFrame: return up * 2.25f;
            case AssemblyKind.Roof: return up * 2.7f;
            case AssemblyKind.Windscreen: return up * 2.65f;
            case AssemblyKind.Door: return up * 2.1f + outward * 1.35f;
            case AssemblyKind.Front: return up * 2.6f + carForward * 0.81f;
            case AssemblyKind.Rear: return up * 1.95f - carForward * 1.5f;
            case AssemblyKind.Seats: return up * 1.0f;
            case AssemblyKind.Cockpit: return up * 1.0f + carForward * 0.45f;
            case AssemblyKind.Wheel: return up * 0.18f + outward * 0.95f;
            case AssemblyKind.Engine: return up * 0.32f + carForward * 0.95f;
            case AssemblyKind.Floor:
            case AssemblyKind.Chassis:
                return Vector3.zero;
            default:
                return GetGenericExplodeOffset(assembly, Vector3.zero, 0);
        }
    }

    // Tagged and unknown assemblies: outward from the car's centre in their own direction (turned by `turn`
    // degrees), just clear of the car's footprint, on a height tier by system: running gear on the floor, the
    // powertrain's systems a little higher, the cabin's in the middle and the body's on top.
    private Vector3 GetGenericExplodeOffset(Node assembly, Vector3 extra, float turn)
    {
        Bounds box = assembly.RestBounds;
        Vector3 away = Vector3.ProjectOnPlane(box.center - carRestCenter, Vector3.up);
        if (away.sqrMagnitude < 0.0025f)
            away = Vector3.Dot(box.center - carRestCenter, carForward) >= 0f ? carForward : -carForward;
        away = Quaternion.AngleAxis(turn, Vector3.up) * away.normalized;
        GetExtentAlong(carRestBounds, away, out float carCenter, out float carHalf);
        GetExtentAlong(box, away, out float center, out float half);
        float shift = Mathf.Max(0.25f, carCenter + carHalf + 0.3f - (center - half));
        Vector3 offset = away * shift + Vector3.up * GetTierLift(assembly) + extra;
        float floor = GetFloorTop() + FloorClearance;
        if (box.min.y + offset.y < floor)
            offset.y = floor - box.min.y;
        return offset;
    }

    private static float GetTierLift(Node assembly)
    {
        switch (assembly.Info.SystemName)
        {
            case "Wheels":
            case "Brakes":
            case "Suspension":
            case "Steering": return 0.15f;
            case "Powertrain":
            case "Driveline":
            case "Exhaust":
            case "Fuel":
            case "Cooling":
            case "Electrical": return 0.3f;
            case "Interior": return 1.0f;
            case "Structure": return 1.6f;
            default: return 2.0f;
        }
    }

    // The exploded view comes apart from the top down: roof and body first, the engine and floor last; tagged
    // assemblies by their tier.
    private static float GetExplodeDelay(Node assembly)
    {
        switch (assembly.Kind)
        {
            case AssemblyKind.Roof: return 0f;
            case AssemblyKind.Windscreen: return 0.04f;
            case AssemblyKind.BodyFrame: return 0.08f;
            case AssemblyKind.Front:
            case AssemblyKind.Rear: return 0.12f;
            case AssemblyKind.Door: return 0.18f;
            case AssemblyKind.Seats: return 0.26f;
            case AssemblyKind.Cockpit: return 0.3f;
            case AssemblyKind.Wheel: return 0.34f;
            case AssemblyKind.Engine: return 0.38f;
            case AssemblyKind.Floor:
            case AssemblyKind.Chassis: return 0.42f;
            default: return Mathf.Lerp(0.42f, 0.2f, Mathf.Clamp01(GetTierLift(assembly) / 2f));
        }
    }

    // The layout, fitted to the room as it is now: CarConcept's own assemblies at their tuned places, kept under
    // ExplodeMaxHeight, off the floor, out of the display cars and furniture and off the viewer; then every other
    // assembly in the first free place outward from the car (further out, turned aside, or a tier up), clear of
    // the assemblies already placed.
    private void FitExplodeLayout()
    {
        GatherKeepOuts();
        Vector3 viewer = viewCamera != null ? viewCamera.transform.position : carRestCenter + carForward * 8f;
        explodeBoxes.Clear();
        explodeOrder.Clear();
        foreach (Node assembly in assemblies)
            if (assembly.Kind != AssemblyKind.Generic)
                explodeOrder.Add(assembly);
        int tuned = explodeOrder.Count;
        // CarConcept's own assemblies from the floor up: what stays, the floor tier, the cabin, the frame, then the
        // panels around and above it.
        explodeOrder.Sort((a, b) =>
            Array.IndexOf(TunedExplodeOrder, a.Kind).CompareTo(Array.IndexOf(TunedExplodeOrder, b.Kind)));
        foreach (Node assembly in assemblies)
            if (assembly.Kind == AssemblyKind.Generic)
                explodeOrder.Add(assembly);
        explodeOrder.Sort(tuned, explodeOrder.Count - tuned, Comparer<Node>.Create((a, b) =>
            HorizontalDistance(a.RestBounds.center, carRestCenter)
                .CompareTo(HorizontalDistance(b.RestBounds.center, carRestCenter))));

        for (int index = 0; index < explodeOrder.Count; index++)
        {
            Node assembly = explodeOrder[index];
            Bounds box = assembly.RestBounds;
            if (!assembly.Relocates)
            {
                assembly.ExplodeOffset = Vector3.zero;
                explodeBoxes.Add(box);
                continue;
            }
            Vector3 offset = index < tuned ? SeparateTuned(assembly, box, FitTunedOffset(assembly, box, viewer), viewer)
                : FitGenericOffset(assembly, box, viewer);
            assembly.ExplodeOffset = offset;
            box.center += offset;
            explodeBoxes.Add(box);
        }
    }

    private Vector3 FitTunedOffset(Node assembly, Bounds box, Vector3 viewer)
    {
        Vector3 offset = GetExplodeOffset(assembly);
        Vector3 horizontal = Vector3.ProjectOnPlane(offset, Vector3.up);
        float vertical = offset.y;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Bounds target = box;
            target.center += horizontal + Vector3.up * vertical;
            if (target.max.y > ExplodeMaxHeight)
                vertical -= target.max.y - ExplodeMaxHeight;
            float floor = GetFloorTop() + FloorClearance;
            if (box.min.y + vertical < floor)
                vertical = floor - box.min.y;
            target = box;
            target.center += horizontal + Vector3.up * vertical;
            if (!IntersectsKeepOut(target))
                break;
            horizontal *= 0.8f;
        }
        Bounds placed = box;
        placed.center += horizontal + Vector3.up * vertical;
        if (GetViewerClearance(placed, viewer) < ViewerCapsuleRadius)
        {
            // Away from the viewer, but never back toward the car (into the tiers that stay there), and far
            // enough even when the viewer stands inside the landing box.
            Vector3 away = Vector3.ProjectOnPlane(placed.center - viewer, Vector3.up);
            if (horizontal.sqrMagnitude > 0.0001f && Vector3.Dot(away, horizontal) < 0f)
                away = Vector3.ProjectOnPlane(away, horizontal.normalized);
            if (away.sqrMagnitude < 0.0001f)
                away = horizontal.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, horizontal)
                    : Vector3.ProjectOnPlane(placed.center - carRestCenter, Vector3.up);
            away.Normalize();
            for (int step = 0; step < 80 && GetViewerClearance(placed, viewer) < ViewerCapsuleRadius; step++)
            {
                horizontal += away * 0.05f;
                placed.center += away * 0.05f;
            }
        }
        return horizontal + Vector3.up * vertical;
    }

    private static readonly AssemblyKind[] TunedExplodeOrder =
    {
        AssemblyKind.Chassis, AssemblyKind.Floor, AssemblyKind.Wheel, AssemblyKind.Engine, AssemblyKind.Seats,
        AssemblyKind.Cockpit, AssemblyKind.BodyFrame, AssemblyKind.Door, AssemblyKind.Rear, AssemblyKind.Front,
        AssemblyKind.Windscreen, AssemblyKind.Roof
    };

    // A tuned place that would touch an assembly placed before it moves on along its own way out (up for those
    // lifted straight up), in 5 cm steps, while it stays in the room.
    private Vector3 SeparateTuned(Node assembly, Bounds box, Vector3 offset, Vector3 viewer)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(offset, Vector3.up);
        Vector3 direction = horizontal.sqrMagnitude > 0.01f ? horizontal.normalized : Vector3.up;
        Vector3 result = offset;
        for (int step = 0; step < 40; step++)
        {
            Bounds target = box;
            target.center += result;
            if (!OverlapsAny(target, explodeBoxes, ExplodeGap))
                return result;
            Vector3 next = result + direction * 0.05f;
            Bounds moved = box;
            moved.center += next;
            if (moved.max.y > ExplodeMaxHeight || IntersectsKeepOut(moved) ||
                GetViewerClearance(moved, viewer) < ViewerCapsuleRadius)
                break;
            result = next;
        }
        return result;
    }

    // Outward in its own direction (further out in steps), then turned aside either way, then a tier up: the first
    // free place on the podium (PodiumInset inside its edge, so nothing lands on the floor round it, where Remy and the
    // visitors walk) wins. When no free place is left on the podium (the assemblies that span the car, such as the
    // sills, lines and harness, with dozens of others already placed), the valid place on it that overlaps the others
    // least wins, also straight up over the car's middle or centred on the podium; only without any valid place on the
    // podium does the valid place that reaches least past its edge count.
    private Vector3 FitGenericOffset(Node assembly, Bounds box, Vector3 viewer)
    {
        Vector3 best = GetGenericExplodeOffset(assembly, Vector3.zero, 0f);
        float bestScore = float.NegativeInfinity;
        Vector3 leastOverlap = best;
        float leastOverlapVolume = float.PositiveInfinity;
        Vector3 leastExcess = best;
        float leastExcessMetres = float.PositiveInfinity;
        for (int tier = 0; tier < ExplodeTiers; tier++)
        for (int turnIndex = 0; turnIndex < GenericTurns.Length; turnIndex++)
        for (int push = 0; push < 12; push++)
        {
            Vector3 extra = Vector3.up * (ExplodeTierStep * tier);
            Vector3 offset = GetGenericExplodeOffset(assembly, extra, GenericTurns[turnIndex]);
            Vector3 away = Vector3.ProjectOnPlane(offset, Vector3.up);
            if (away.sqrMagnitude > 1e-6f)
                offset += away.normalized * (0.15f * push);
            Bounds target = box;
            target.center += offset;
            bool onPodium = IsOnPodium(target);
            float score = ScorePlacement(target, viewer, ExplodeMaxHeight, ViewerCapsuleRadius, out bool valid);
            bool free = !OverlapsAny(target, explodeBoxes, ExplodeGap);
            if (valid && free && onPodium)
                return offset;
            if (valid && onPodium)
            {
                float volume = OverlapVolume(target, explodeBoxes, ExplodeGap);
                if (volume < leastOverlapVolume)
                {
                    leastOverlapVolume = volume;
                    leastOverlap = offset;
                }
            }
            else if (valid)
            {
                float excess = GetPodiumExcess(target);
                if (excess < leastExcessMetres)
                {
                    leastExcessMetres = excess;
                    leastExcess = offset;
                }
            }
            score += (free ? 50f : 0f) + (onPodium ? 60f : 0f);
            score -= push * 0.05f + tier * 0.5f + turnIndex * 0.1f;
            if (score > bestScore)
            {
                bestScore = score;
                best = offset;
            }
            // Further out only leaves the podium further behind.
            if (!onPodium)
                break;
        }
        // Straight up over the car's middle, and also centred on the podium (the car need not stand at its centre).
        Vector3 toPodium = hasPodium ? new Vector3(podiumCenter.x - box.center.x, 0f, podiumCenter.y - box.center.z)
            : Vector3.zero;
        for (int candidate = 0; candidate < ExplodeTiers * 4; candidate++)
        {
            int tier = candidate >> 1;
            Vector3 offset = Vector3.up * (ExplodeTierStep * 0.5f * tier) + ((candidate & 1) == 1 ? toPodium : Vector3.zero);
            Bounds target = box;
            target.center += offset;
            ScorePlacement(target, viewer, ExplodeMaxHeight, ViewerCapsuleRadius, out bool valid);
            if (!valid)
                continue;
            if (!IsOnPodium(target))
            {
                float excess = GetPodiumExcess(target);
                if (excess < leastExcessMetres)
                {
                    leastExcessMetres = excess;
                    leastExcess = offset;
                }
                continue;
            }
            float volume = OverlapVolume(target, explodeBoxes, ExplodeGap);
            if (volume <= 0f)
                return offset;
            if (volume < leastOverlapVolume)
            {
                leastOverlapVolume = volume;
                leastOverlap = offset;
            }
        }
        // An assembly longer than the podium allows (the harness, the power distribution and the fuel lines run from
        // the nose to the tail) overhangs its edge as little as it can, over the car's middle, rather than landing on
        // the floor round it.
        return leastOverlapVolume < float.PositiveInfinity ? leastOverlap
            : leastExcessMetres < float.PositiveInfinity ? leastExcess : best;
    }

    // How far (horizontally) the box reaches past the line PodiumInset inside the podium's edge; 0 on the podium.
    private float GetPodiumExcess(Bounds box)
    {
        if (!hasPodium)
            return 0f;
        float far = 0f;
        for (int i = 0; i < 4; i++)
        {
            float x = ((i & 1) == 0 ? box.min.x : box.max.x) - podiumCenter.x;
            float z = ((i & 2) == 0 ? box.min.z : box.max.z) - podiumCenter.y;
            far = Mathf.Max(far, Mathf.Sqrt(x * x + z * z));
        }
        return Mathf.Max(0f, far - (podiumRadius - PodiumInset));
    }

    // Total volume the box (grown by gap) shares with the boxes.
    private static float OverlapVolume(Bounds box, List<Bounds> boxes, float gap)
    {
        box.Expand(gap);
        float volume = 0f;
        foreach (Bounds other in boxes)
        {
            Vector3 size = Vector3.Min(box.max, other.max) - Vector3.Max(box.min, other.min);
            if (size.x > 0f && size.y > 0f && size.z > 0f)
                volume += size.x * size.y * size.z;
        }
        return volume;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x;
        float z = a.z - b.z;
        return Mathf.Sqrt(x * x + z * z);
    }

    // ---------------------------------------------------------------- bounds

    private static Bounds GetWorldBoundsOfParts(List<Part> source)
    {
        bool found = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Part part in source)
        foreach (Renderer renderer in part.Renderers)
        {
            if (renderer == null) continue;
            if (!found) { result = renderer.bounds; found = true; }
            else result.Encapsulate(renderer.bounds);
        }
        return result;
    }

    // World bounds of the registered parts' renderers under (and including) `root`.
    private bool TryGetBounds(Transform root, out Bounds bounds)
    {
        bool found = false;
        bounds = new Bounds(root.position, Vector3.zero);
        foreach (Part part in parts)
        {
            if (part.Target != root && !part.Target.IsChildOf(root))
                continue;
            foreach (Renderer renderer in part.Renderers)
            {
                if (renderer == null) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
        }
        return found;
    }

    private static Vector3 GetBoxCorner(Bounds bounds, int index)
    {
        Vector3 extents = bounds.extents;
        return bounds.center + new Vector3((index & 1) == 0 ? -extents.x : extents.x,
            (index & 2) == 0 ? -extents.y : extents.y, (index & 4) == 0 ? -extents.z : extents.z);
    }

    private float GetShowroomFloorHeight()
    {
        GameObject floor = GameObject.Find("Showroom Floor");
        if (floor != null)
        {
            Collider collider = floor.GetComponent<Collider>();
            if (collider != null) return collider.bounds.max.y;
            Renderer renderer = floor.GetComponent<Renderer>();
            if (renderer != null) return renderer.bounds.max.y;
            return floor.transform.position.y;
        }
        return carRoot != null ? carRoot.position.y : 0f;
    }

    private static bool Has(string value, string term)
    {
        return value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        foreach (string term in terms)
            if (value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
}
