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
        // An assembly travels first and comes apart on the way; coming back, it closes first and then travels home.
        // A level that closes while another opens closes first. A single kind of move takes the whole time.
        phasedMotion = (travel && (spread || close)) || (spread && close);

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
        transitionSpan = Mathf.Max(0.3f, transitionLength - maxDelay);
        transitionClock = 0f;
        transitionT = movingNodes.Count > 0 || movingTracks.Count > 0 ? 0f : 1f;
        if (transitionT >= 1f)
            FinishTransition();
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
            if (phasedMotion)
            {
                float first = Smooth(Mathf.Clamp01(local / FirstPhaseShare));
                float second = Smooth(Mathf.Clamp01((local - SecondPhaseStart) / (1f - SecondPhaseStart)));
                bool isAssembly = node == node.Assembly;
                amount = isAssembly == node.Spreading ? first : second;
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
    // first toward the middle of the view. Each deeper level comes out the same way, beside the one before.
    private void ComputePull(Node node)
    {
        placedWhileInspecting |= CarExplorerInspect.Active;
        node.PullOffset = Vector3.zero;
        Node assembly = node.Assembly;
        if (assembly == null || node == assembly || viewCamera == null)
            return;
        Bounds own = GetPlannedBounds(node, true);
        Vector3 eye = viewCamera.transform.position;
        GatherFocusKeepOuts();
        GatherPartBoxes(node);
        float ceiling = view == CarExplorerView.Exploded ? ExplodeMaxHeight : FocusMaxHeight;
        float floor = GetFloorTop() + FloorClearance;
        float lowest = floor + PullMinHeight;
        float highest = Mathf.Max(lowest, eye.y - BelowEye);
        Vector3 toLevel = Vector3.ProjectOnPlane(own.center - eye, Vector3.up);
        float distanceNow = toLevel.magnitude;
        Vector3 look = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up);
        Vector3 bearing = distanceNow > 0.01f ? toLevel / distanceNow
            : look.sqrMagnitude > 0.0001f ? look.normalized : carForward;
        float toward = look.sqrMagnitude > 0.0001f && Vector3.SignedAngle(bearing, look, Vector3.up) < 0f ? -1f : 1f;
        float halfWidth = Mathf.Max(own.extents.x, own.extents.z);
        float nearest = PullViewerClearance + halfWidth + 0.05f;
        float reach = Mathf.Max(PullEyeDistance, nearest + 0.15f, own.extents.magnitude * 3.2f);
        // In the exploded view the level stays near its assembly; a taken-out assembly's comes to the visitor.
        if (view == CarExplorerView.Exploded)
            reach = Mathf.Max(reach, distanceNow - PullTravelMax);
        float baseHeight = Mathf.Clamp(own.center.y, lowest, highest);
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
                    float score = ScoreView(moved, eye, ceiling, PullViewerClearance, out bool valid) -
                                  0.3f * t - 0.2f * n - 0.1f * h;
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

    // Where a clicked assembly goes: out of the car in its own direction (a door or wheel to its side, the V6
    // forward) at a comfortable height, or up above the car (the front and rear sections like a raised hood, the
    // cabin assemblies, the roof, the windscreen), with its centre BelowEye under the eye at most - the first of these
    // that is clear of the viewer, the room, the other display cars and the rest of the car, and in view (ScoreView).
    // When none is, it goes out of the car toward any bearing round it, at a comfortable height: the free place in
    // view nearest the middle of the view wins. `spread` is the assembly's box with its open levels, as if it stood in
    // the car. Places whose centre is further than maxDistance from the eye are not tried (RefitTakenOut keeps an
    // opening assembly near the visitor).
    private Vector3 ComputeFocusOffset(Node assembly, Bounds spread, float maxDistance = float.PositiveInfinity)
    {
        placedWhileInspecting |= CarExplorerInspect.Active;
        Vector3 viewer = viewCamera != null ? viewCamera.transform.position : carRestCenter + carForward * 6f;
        assembly.FocusPivot = assembly.RestBounds.center;
        assembly.FocusYaw = GetFocusYaw(assembly, assembly.FocusPivot, viewer);
        if (!assembly.Relocates)
            return Vector3.zero;
        spread = RotateBounds(spread, assembly.FocusYaw, assembly.FocusPivot);
        GatherFocusKeepOuts();
        GatherPartBoxes(assembly);

        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        for (int candidate = 0; candidate < 3; candidate++)
        {
            Vector3 offset = GetOwnFocusOffset(assembly, spread, candidate, viewer);
            Bounds target = spread;
            target.center += offset;
            if (Vector3.Distance(viewer, target.center) > maxDistance)
                continue;
            float score = ScoreView(target, viewer, FocusMaxHeight, FocusViewerClearance, out bool valid) -
                          candidate * 0.1f;
            if (valid)
                return offset;
            if (score > bestScore)
            {
                bestScore = score;
                best = offset;
            }
        }
        bool found = false;
        for (int bearing = 0; bearing < RingBearings; bearing++)
        {
            Vector3 direction = Quaternion.AngleAxis(360f * bearing / RingBearings, Vector3.up) * carForward;
            for (int level = 0; level < RingHeights.Length; level++)
            {
                Vector3 offset = OutOfCar(spread, direction, RingHeights[level]);
                Bounds target = spread;
                target.center += offset;
                // In view, then nearest the middle of the view and not far beyond reading distance.
                float distance = Vector3.Distance(viewer, target.center);
                if (distance > maxDistance)
                    continue;
                float score = ScoreView(target, viewer, FocusMaxHeight, FocusViewerClearance, out bool valid) -
                              10f * GetViewOffCentre(target) - 4f * Mathf.Max(0f, distance - 3f) -
                              0.2f * level - 1f;
                if (valid)
                    score += 1000f;
                if (score > bestScore && (valid || !found))
                {
                    found |= valid;
                    bestScore = score;
                    best = offset;
                }
            }
        }
        return best;
    }

    // An assembly's own ways out, in order. Places above the car stay below the eye and slide out along the
    // assembly's way until they are clear of the rest of the car.
    private Vector3 GetOwnFocusOffset(Node assembly, Bounds spread, int candidate, Vector3 viewer)
    {
        Vector3 outward = carRight * assembly.Side;
        Vector3 away = Vector3.ProjectOnPlane(spread.center - carRestCenter, Vector3.up);
        away = away.sqrMagnitude > 0.01f ? away.normalized : carForward;
        Vector3 toViewer = GetSideTowardViewer(viewer);
        switch (assembly.Kind)
        {
            case AssemblyKind.Wheel:
                return candidate == 0 ? OutOfCar(spread, outward, 0.95f)
                    : candidate == 1 ? OutOfCar(spread, outward, 0.95f) * 0.75f + Vector3.up * 0.25f
                    : AboveCarClear(spread, 0.25f, Vector3.zero, outward, viewer);
            case AssemblyKind.Door:
                return candidate == 0 ? OutOfCar(spread, outward, 1.15f)
                    : candidate == 1 ? AboveCarClear(spread, 0.25f, Vector3.zero, outward, viewer)
                    : OutOfCar(spread, outward, 1.6f);
            // The front and rear sections lift off like a raised hood or tailgate, so the bay stays in view; else
            // they go out of the car on their own end, or that end and aside from the viewer.
            case AssemblyKind.Front:
                return candidate == 0 ? AboveCarClear(spread, 0.3f, carForward * 0.25f, carForward, viewer)
                    : candidate == 1 ? OutOfCar(spread, carForward, 1.15f)
                    : OutOfCar(spread, carForward, 1.15f) - toViewer * 1.2f;
            case AssemblyKind.Rear:
                return candidate == 0 ? AboveCarClear(spread, 0.3f, -carForward * 0.25f, -carForward, viewer)
                    : candidate == 1 ? OutOfCar(spread, -carForward, 1.15f)
                    : OutOfCar(spread, -carForward, 1.15f) - toViewer * 1.2f;
            // Forward out of the bay; for a viewer standing in front, forward and aside from them; else lifted out
            // of the bay and forward.
            case AssemblyKind.Engine:
                return candidate == 0 ? OutOfCar(spread, carForward, 1.2f)
                    : candidate == 1 ? OutOfCar(spread, carForward, 1.2f) - toViewer * 1.1f
                    : AboveCarClear(spread, 0.2f, Vector3.zero, carForward, viewer);
            case AssemblyKind.Windscreen:
                return AboveCarClear(spread, 0.25f, carForward * (candidate == 0 ? 0.35f : candidate == 1 ? 0f : -0.3f),
                    candidate == 2 ? -carForward : carForward, viewer);
            case AssemblyKind.Generic:
                // Tagged and unknown assemblies: out of the car on their own side, a little aside from a viewer
                // standing there, else above the car.
                return candidate == 0 ? OutOfCar(spread, away, 1.1f)
                    : candidate == 1 ? OutOfCar(spread, away, 1.1f) - toViewer * 0.9f
                    : AboveCarClear(spread, 0.25f, Vector3.zero, away, viewer);
            default:
                // Roof, body frame, seats, cockpit: straight up out of the car, then out toward the viewer's side.
                return AboveCarClear(spread, 0.25f + 0.3f * candidate, Vector3.zero, toViewer, viewer);
        }
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

    // After a level opens or closes, the taken-out assembly stays where it is if its new size still fits there and,
    // while it is what the visitor looks at (in one piece or apart one level), is still in view; else it takes a new
    // place. True when it moved.
    private bool RefitTakenOut()
    {
        if (takenOut == null || view == CarExplorerView.Exploded || !takenOut.Relocates || openPath.Count > 1)
            return false;
        Bounds spread = GetPlannedBounds(takenOut, false);
        Bounds rotated = RotateBounds(spread, takenOut.FocusYaw, takenOut.FocusPivot);
        rotated.center += takenOut.FocusOffset;
        Vector3 viewer = viewCamera != null ? viewCamera.transform.position : carRestCenter + carForward * 6f;
        GatherFocusKeepOuts();
        GatherPartBoxes(takenOut);
        ScoreView(rotated, viewer, FocusMaxHeight, FocusViewerClearance, out bool valid);
        if (valid)
            return false;
        float stayShare = GetViewShare(rotated);
        Vector3 before = takenOut.FocusOffset;
        float yaw = takenOut.FocusYaw;
        Vector3 pivot = takenOut.FocusPivot;
        // An opening level does not send the assembly away from the visitor: only places at most RefitFurther further
        // from the eye than where it stands count; without a valid one, the best of them moves it only when more of it
        // is in the free view there than where it came out.
        float limit = Vector3.Distance(viewer, rotated.center) + RefitFurther;
        takenOut.FocusOffset = ComputeFocusOffset(takenOut, spread, limit);
        Bounds moved = RotateBounds(spread, takenOut.FocusYaw, takenOut.FocusPivot);
        moved.center += takenOut.FocusOffset;
        ScoreView(moved, viewer, FocusMaxHeight, FocusViewerClearance, out bool moveValid);
        if (Vector3.Distance(viewer, moved.center) > limit + 0.01f ||
            (!moveValid && GetViewShare(moved) <= stayShare + 0.05f))
        {
            takenOut.FocusOffset = before;
            takenOut.FocusYaw = yaw;
            takenOut.FocusPivot = pivot;
            return false;
        }
        return takenOut.FocusOffset != before || takenOut.FocusYaw != yaw;
    }

    // The cockpit turns its dashboard and wheel (the driver's side) to the viewer, the seats their fronts; the
    // rest keep their orientation, so a door or wheel shows its outer face and spreads across the view.
    private float GetFocusYaw(Node assembly, Vector3 pivot, Vector3 viewer)
    {
        Vector3 face;
        if (assembly.Kind == AssemblyKind.Cockpit)
            face = -carForward;
        else if (assembly.Kind == AssemblyKind.Seats)
            face = carForward;
        else
            return 0f;
        Vector3 toViewer = Vector3.ProjectOnPlane(viewer - pivot, Vector3.up);
        if (toViewer.sqrMagnitude < 0.01f)
            return 0f;
        float yaw = Vector3.SignedAngle(face, toViewer, Vector3.up);
        // Small turns are left out: they only make the assembly look askew.
        return Mathf.Abs(yaw) < 20f ? 0f : yaw;
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

    // The car's side toward the viewer (the V6 steps aside to the other one).
    private Vector3 GetSideTowardViewer(Vector3 viewer)
    {
        return Vector3.Dot(viewer - carRestCenter, carRight) >= 0f ? carRight : -carRight;
    }

    // Moves `spread` out of the car along `direction` until it clears the car's rest bounds by FocusClearance, with
    // its centre at `height` above the floor (kept off the floor and under FocusMaxHeight).
    private Vector3 OutOfCar(Bounds spread, Vector3 direction, float height)
    {
        GetExtentAlong(carRestBounds, direction, out float carCenter, out float carHalf);
        GetExtentAlong(spread, direction, out float center, out float half);
        float shift = Mathf.Max(0f, carCenter + carHalf + FocusClearance - (center - half));
        float floor = GetFloorTop();
        float lift = floor + height - spread.center.y;
        lift = Mathf.Max(lift, floor + FloorClearance - spread.min.y);
        lift = Mathf.Min(lift, FocusMaxHeight - spread.max.y);
        return direction * shift + Vector3.up * lift;
    }

    private static void GetExtentAlong(Bounds bounds, Vector3 axis, out float center, out float half)
    {
        center = Vector3.Dot(bounds.center, axis);
        Vector3 e = bounds.extents;
        half = Mathf.Abs(e.x * axis.x) + Mathf.Abs(e.y * axis.y) + Mathf.Abs(e.z * axis.z);
    }

    // Straight up until `spread` clears the car's roof by `gap`, with its centre BelowEye under the eye at most (so
    // the visitor looks down on it; lower than the roof, the place usually runs into the car and is not taken).
    private Vector3 AboveCar(Bounds spread, float gap, Vector3 eye)
    {
        float lift = Mathf.Max(0f, carRestBounds.max.y + gap - spread.min.y);
        return Vector3.up * Mathf.Min(lift, Mathf.Max(0f, eye.y - BelowEye - spread.center.y));
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

    // A place above the car (AboveCar, then `shift`) that stays below the eye: where it runs into the rest of the car
    // it slides out along `direction` in 10 cm steps, up to 1.5 m, and it comes down in 10 cm steps (three at most)
    // while it reaches out of the view; the first place clear of the other parts and in view wins, else the first
    // clear one.
    private Vector3 AboveCarClear(Bounds spread, float gap, Vector3 shift, Vector3 direction, Vector3 eye)
    {
        Vector3 lift = AboveCar(spread, gap, eye);
        Vector3 first = shift + lift;
        bool clearFound = false;
        for (int drop = 0; drop < 4; drop++)
        {
            Vector3 lowered = lift - Vector3.up * (0.1f * drop);
            if (drop > 0 && lowered.y < 0f)
                break;
            for (int step = 0; step <= 15; step++)
            {
                Vector3 offset = shift + lowered + direction * (0.1f * step);
                Bounds target = spread;
                target.center += offset;
                if (HitsParts(target))
                    continue;
                if (GetViewShare(target) >= ViewShareRequired)
                    return offset;
                if (!clearFound)
                {
                    clearFound = true;
                    first = offset;
                }
            }
        }
        return first;
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
        Rect free = GetFreeView();
        float x = Mathf.Max(0f, Mathf.Min(rect.xMax, free.xMax) - Mathf.Max(rect.xMin, free.xMin));
        float y = Mathf.Max(0f, Mathf.Min(rect.yMax, free.yMax) - Mathf.Max(rect.yMin, free.yMin));
        float area = rect.width * rect.height;
        return area > 1e-8f ? x * y / area : free.Contains(rect.center) ? 1f : 0f;
    }

    // How far the box's screen centre lies from the middle of the free view (viewport units; 2 when behind the eye).
    private float GetViewOffCentre(Bounds box)
    {
        if (viewCamera == null || !TryGetViewRect(box, out Rect rect))
            return 2f;
        return Vector2.Distance(rect.center, GetFreeView().center);
    }

    private bool TryGetViewRect(Bounds box, out Rect rect)
    {
        rect = default;
        float near = viewCamera.nearClipPlane;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Vector3 point = viewCamera.WorldToViewportPoint(GetBoxCorner(box, i));
            if (point.z < near)
                return false;
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
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
