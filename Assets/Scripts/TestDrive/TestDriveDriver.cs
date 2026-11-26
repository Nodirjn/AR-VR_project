using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A seated person in a car, made from one of the city's pedestrians: hips on the seat, back against the reclined
/// seat back, feet on the pedals and hands on the steering wheel (solved with the same two-bone method the cyclists
/// use). The visitor's own body has its head hidden, so looking down in the headset shows the torso, arms and legs,
/// and its hands follow the steering wheel. Traffic drivers sit behind their own car's steering wheel (found by
/// <see cref="TestDriveCabin"/>); a car without a recognisable cabin is left empty, and far drivers are culled.
/// </summary>
public sealed class TestDriveDriver : MonoBehaviour
{
    private const float HandFollowLimit = 100f;

    private Transform car;
    private Transform hips, spine, neck, head;
    private Transform leftUpper, leftLower, leftHand, rightUpper, rightLower, rightHand;
    private Transform leftThigh, leftShin, leftFoot, leftToe, rightThigh, rightShin, rightFoot, rightToe;
    private Quaternion leftUpperBase, leftLowerBase, rightUpperBase, rightLowerBase, leftHandBase, rightHandBase;
    private TestDriveVehicle vehicle;
    private Vector3 wheelCentre;               // car space
    private Quaternion wheelFrame;             // car space, unturned
    private float wheelRadius;
    private Vector3 staticLeftGrip, staticRightGrip; // car space, when there is no steering pivot

    // ------------------------------------------------------------------ templates

    private static readonly List<GameObject> templates = new List<GameObject>();
    private static Transform templateHolder;

    /// <summary>Copies a few of the scene's walking people (inactive, components stripped) while they are all there.</summary>
    public static void CollectTemplates()
    {
        if (templates.Count > 0) return;
        if (templateHolder == null)
        {
            var holderObject = new GameObject("Test Drive Driver Templates");
            holderObject.SetActive(false);
            templateHolder = holderObject.transform;
        }
        var sources = new List<Animator>();
        foreach (ExteriorPedestrianAgent agent in Object.FindObjectsByType<ExteriorPedestrianAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Animator animator = agent.GetComponentInChildren<Animator>(true);
            if (animator != null && agent.GetComponentInChildren<SkinnedMeshRenderer>(true) != null && HasHumanRig(animator)) sources.Add(animator);
            if (sources.Count >= 6) break;
        }
        if (sources.Count == 0)
        {
            foreach (Animator animator in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (animator.avatar == null || !animator.avatar.isHuman) continue;
                if (animator.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
                sources.Add(animator);
                if (sources.Count >= 3) break;
            }
        }
        for (int i = 0; i < sources.Count; i++)
        {
            GameObject copy = Object.Instantiate(sources[i].gameObject, templateHolder);
            copy.name = "Driver Template " + i;
            Strip(copy);
            templates.Add(copy);
        }
    }

    private static bool HasHumanRig(Animator animator)
    {
        if (animator.avatar != null && animator.avatar.isHuman) return true;
        foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
            if (t.name == "Hips" || t.name.EndsWith(":Hips", System.StringComparison.Ordinal)) return true;
        return false;
    }

    private static void Strip(GameObject copy)
    {
        foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
        foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
        foreach (Rigidbody body in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
        foreach (LODGroup lod in copy.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
        foreach (AudioSource audio in copy.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(audio);
    }

    // ------------------------------------------------------------------ seating

    /// <summary>The visitor's body in the test car. Returns null when the scene has no people to copy.</summary>
    public static TestDriveDriver SeatVisitor(TestDriveVehicle vehicle)
    {
        CollectTemplates();
        if (templates.Count == 0 || vehicle.EyeAnchor == null) return null;
        Transform root = vehicle.transform;
        Vector3 eye = vehicle.EyeAnchor.localPosition;
        TestDriveDriver driver = Create(templates[0], root, "Driver Body");
        if (driver == null) return null;
        driver.vehicle = vehicle;
        if (vehicle.SteeringPivot != null)
        {
            driver.wheelCentre = vehicle.SteeringPivot.localPosition;
            driver.wheelFrame = vehicle.SteeringPivot.localRotation;
            driver.wheelRadius = vehicle.SteeringRadius;
        }
        else
        {
            driver.staticLeftGrip = eye + new Vector3(-0.19f, -0.3f, 0.45f);
            driver.staticRightGrip = eye + new Vector3(0.19f, -0.3f, 0.45f);
        }
        driver.SeatAt(eye, vehicle.SeatCushion);
        if (driver.head != null) driver.head.localScale = Vector3.one * 0.001f;
        driver.WidenBounds();
        return driver;
    }

    /// <summary>Puts a driver behind the steering wheel of every traffic car that has none yet.</summary>
    public static void PopulateTraffic()
    {
        CollectTemplates();
        if (templates.Count == 0) return;
        bool quest = QualitySettings.GetQualityLevel() <= 1 || Application.platform == RuntimePlatform.Android;
        int index = 0;
        foreach (ExteriorTrafficCarAgent agent in Object.FindObjectsByType<ExteriorTrafficCarAgent>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            Transform car = agent.transform;
            if (car.Find("Traffic Driver") != null) continue;
            var renderers = CarRenderers(car);
            Bounds local;
            if (!LocalBounds(car, renderers, out local) || local.size.z < 3f || local.size.x < 1.4f) continue;
            // The model's own steering wheel and seat; a car without a recognisable cabin stays without a driver
            // rather than getting one guessed onto its roof.
            TestDriveCabin.Layout layout = TestDriveCabin.ForTraffic(car, renderers, local);
            if (layout == null) continue;
            TestDriveDriver driver = Create(templates[index++ % templates.Count], car, "Traffic Driver");
            if (driver == null) continue;
            TestDriveCabin.SteeringWheel wheel = layout.wheel;
            Quaternion frame = Quaternion.LookRotation(wheel.axis, Vector3.up);
            float r = wheel.radius;
            driver.staticLeftGrip = wheel.centre + frame * new Vector3(-r * 0.94f, r * 0.34f, -0.02f);
            driver.staticRightGrip = wheel.centre + frame * new Vector3(r * 0.94f, r * 0.34f, -0.02f);
            driver.SeatAt(layout.eye, layout.cushion);
            // Last check: the head must be inside the cabin.
            Vector3 headLocal = car.InverseTransformPoint(driver.head != null ? driver.head.position : driver.neck.position);
            if (headLocal.y > local.max.y - 0.04f || headLocal.y < local.min.y + 0.5f)
            {
                Debug.LogWarning("[Test Drive] Driver did not fit in " + car.name + "; left empty.");
                Object.Destroy(driver.gameObject);
                continue;
            }
            driver.WidenBounds();
            driver.enabled = false;   // static pose: nothing to update per frame
            foreach (SkinnedMeshRenderer skin in driver.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.shadowCastingMode = quest ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            LODGroup lod = driver.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(quest ? 0.03f : 0.012f, driver.GetComponentsInChildren<Renderer>(true)) });
            lod.RecalculateBounds();
        }
    }

    private static List<MeshRenderer> CarRenderers(Transform car)
    {
        var list = new List<MeshRenderer>();
        foreach (MeshRenderer renderer in car.GetComponentsInChildren<MeshRenderer>())
        {
            if (TestDriveWheelRig.IsWheelPart(renderer.transform, car)) continue;
            bool skip = false;
            for (Transform t = renderer.transform; t != null && t != car; t = t.parent)
                if (t.name.Contains("Wheel") || t.name == "Traffic Driver") { skip = true; break; }
            if (!skip) list.Add(renderer);
        }
        return list;
    }

    /// <summary>
    /// Seats the person for an eye point and seat cushion (car space): hips sunk a little into the cushion, the head
    /// joint 8 cm under and 7 cm behind the eye, the back reclined 14 degrees, a 1.75 m person.
    /// </summary>
    private void SeatAt(Vector3 eye, float cushion)
    {
        const float torsoLength = 0.58f, recline = 14f;
        Vector3 headAt = eye + new Vector3(0f, -0.08f, -0.07f);
        float hipY = Mathf.Min(float.IsNaN(cushion) ? eye.y - 0.6f : Mathf.Max(cushion + 0.05f, eye.y - 0.66f), eye.y - 0.56f);
        var hip = new Vector3(eye.x, hipY, headAt.z + Mathf.Sin(recline * Mathf.Deg2Rad) * torsoLength);
        Pose(hip, torsoLength, recline);
    }

    private void WidenBounds()
    {
        foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Transform bone = skin.rootBone != null ? skin.rootBone : skin.transform;
            skin.localBounds = new Bounds(Vector3.zero, Vector3.one * (2.5f / Mathf.Max(0.0001f, bone.lossyScale.x)));
            skin.updateWhenOffscreen = false;
        }
    }

    private static bool LocalBounds(Transform root, List<MeshRenderer> renderers, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        Matrix4x4 toLocal = root.worldToLocalMatrix;
        foreach (MeshRenderer renderer in renderers)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            Bounds b = TestDriveWheelRig.CarBox(toLocal * renderer.transform.localToWorldMatrix, filter.sharedMesh.bounds);
            if (!found) { bounds = b; found = true; }
            else bounds.Encapsulate(b);
        }
        return found;
    }

    private static TestDriveDriver Create(GameObject template, Transform parent, string name)
    {
        GameObject person = Object.Instantiate(template, templateHolder);
        person.name = name;
        Transform t = person.transform;
        t.SetParent(parent, false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        person.SetActive(true);
        TestDriveDriver driver = person.AddComponent<TestDriveDriver>();
        driver.car = parent;
        if (!driver.Bind()) { Object.Destroy(person); return null; }
        return driver;
    }

    private bool Bind()
    {
        Animator animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        Animator human = null;
        if (animator != null)
        {
            animator.enabled = true;
            if (animator.isHuman) human = animator;
        }
        hips = Bone(human, HumanBodyBones.Hips, "Hips");
        spine = Bone(human, HumanBodyBones.Spine, "Spine");
        neck = Bone(human, HumanBodyBones.Neck, "Neck");
        head = Bone(human, HumanBodyBones.Head, "Head");
        leftUpper = Bone(human, HumanBodyBones.LeftUpperArm, "LeftArm");
        leftLower = Bone(human, HumanBodyBones.LeftLowerArm, "LeftForeArm");
        leftHand = Bone(human, HumanBodyBones.LeftHand, "LeftHand");
        rightUpper = Bone(human, HumanBodyBones.RightUpperArm, "RightArm");
        rightLower = Bone(human, HumanBodyBones.RightLowerArm, "RightForeArm");
        rightHand = Bone(human, HumanBodyBones.RightHand, "RightHand");
        leftThigh = Bone(human, HumanBodyBones.LeftUpperLeg, "LeftUpLeg");
        leftShin = Bone(human, HumanBodyBones.LeftLowerLeg, "LeftLeg");
        leftFoot = Bone(human, HumanBodyBones.LeftFoot, "LeftFoot");
        leftToe = Bone(human, HumanBodyBones.LeftToes, "LeftToeBase");
        rightThigh = Bone(human, HumanBodyBones.RightUpperLeg, "RightUpLeg");
        rightShin = Bone(human, HumanBodyBones.RightLowerLeg, "RightLeg");
        rightFoot = Bone(human, HumanBodyBones.RightFoot, "RightFoot");
        rightToe = Bone(human, HumanBodyBones.RightToes, "RightToeBase");
        if (animator != null) animator.enabled = false;
        return hips != null && spine != null && neck != null && leftThigh != null && rightThigh != null &&
               leftShin != null && rightShin != null && leftFoot != null && rightFoot != null &&
               leftUpper != null && leftLower != null && leftHand != null && rightUpper != null && rightLower != null && rightHand != null;
    }

    private Transform Bone(Animator human, HumanBodyBones bone, string mixamoName)
    {
        Transform found = human != null ? human.GetBoneTransform(bone) : null;
        if (found != null) return found;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == mixamoName || t.name.EndsWith(":" + mixamoName, System.StringComparison.Ordinal)) return t;
        return null;
    }

    /// <summary>Scales the person to <paramref name="torsoLength"/> (hips to head joint), sits the hips at
    /// <paramref name="hipPoint"/> (car space) and solves the torso, legs and arms.</summary>
    private void Pose(Vector3 hipPoint, float torsoLength, float recline)
    {
        Vector3 right = car.right, up = car.up, forward = car.forward;
        Transform top = head != null ? head : neck;
        float torso = Vector3.Distance(hips.position, top.position);
        if (torso > 1e-5f) transform.localScale *= torsoLength / torso;

        // Pelvis square to the car, tipped back with the seat.
        Vector3 across = rightThigh.position - leftThigh.position;
        hips.rotation = Quaternion.FromToRotation(across, right) * hips.rotation;
        Vector3 spineUp = Vector3.ProjectOnPlane(spine.position - hips.position, right);
        if (spineUp.sqrMagnitude > 1e-6f)
            hips.rotation = Quaternion.FromToRotation(spineUp, Quaternion.AngleAxis(-recline * 0.6f, right) * up) * hips.rotation;
        transform.position += car.TransformPoint(hipPoint) - hips.position;

        // Back against the seat back, head upright.
        Vector3 back = Quaternion.AngleAxis(-recline, right) * up;
        spine.rotation = Quaternion.FromToRotation(neck.position - spine.position, back) * spine.rotation;
        if (head != null)
            neck.rotation = Quaternion.FromToRotation(head.position - neck.position, Quaternion.AngleAxis(-4f, right) * up) * neck.rotation;

        // Legs out to the pedals, knees up and a little apart.
        Leg(leftThigh, leftShin, leftFoot, leftToe, -1f, forward, up, right);
        Leg(rightThigh, rightShin, rightFoot, rightToe, 1f, forward, up, right);

        // Arms rest along the body first, then reach for the wheel.
        leftUpperBase = leftUpper.localRotation;
        leftLowerBase = leftLower.localRotation;
        rightUpperBase = rightUpper.localRotation;
        rightLowerBase = rightLower.localRotation;
        leftHandBase = leftHand.localRotation;
        rightHandBase = rightHand.localRotation;
        Arms();
    }

    private void Leg(Transform thigh, Transform shin, Transform foot, Transform toe, float side, Vector3 forward, Vector3 up, Vector3 right)
    {
        float length = Vector3.Distance(thigh.position, shin.position) + Vector3.Distance(shin.position, foot.position);
        Vector3 reach = Quaternion.AngleAxis(6f, right) * forward;    // thighs along the cushion, shins down to the pedals
        Vector3 ankle = thigh.position + reach * (0.88f * length) + right * (side * 0.04f);
        SolveLimb(thigh, shin, foot, ankle, up * 1.2f + forward * 0.4f + right * (side * 0.25f), true);
        if (toe != null)
            foot.rotation = Quaternion.FromToRotation(toe.position - foot.position, (forward + up * 0.45f).normalized) * foot.rotation;
    }

    private void Arms()
    {
        leftUpper.localRotation = leftUpperBase;
        leftLower.localRotation = leftLowerBase;
        leftHand.localRotation = leftHandBase;
        rightUpper.localRotation = rightUpperBase;
        rightLower.localRotation = rightLowerBase;
        rightHand.localRotation = rightHandBase;
        Vector3 leftGrip, rightGrip, wheelNormal;
        Grips(out leftGrip, out rightGrip, out wheelNormal);
        Vector3 down = -car.up;
        Vector3 leftWrist = SolveLimb(leftUpper, leftLower, leftHand, leftGrip, down - car.right * 0.7f, false);
        Vector3 rightWrist = SolveLimb(rightUpper, rightLower, rightHand, rightGrip, down + car.right * 0.7f, false);
        // Hands continue the forearm, bent slightly over the rim.
        AlignHand(leftHand, leftLower, leftWrist, wheelNormal);
        AlignHand(rightHand, rightLower, rightWrist, wheelNormal);
    }

    private void AlignHand(Transform hand, Transform lower, Vector3 wrist, Vector3 wheelNormal)
    {
        if (hand.childCount == 0) return;
        Transform finger = hand.GetChild(hand.childCount > 2 ? 2 : 0);
        Vector3 along = (wrist - lower.position).normalized;
        Vector3 target = Vector3.Slerp(along, wheelNormal, 0.35f);
        hand.rotation = Quaternion.FromToRotation(finger.position - hand.position, target) * hand.rotation;
    }

    private void Grips(out Vector3 left, out Vector3 right, out Vector3 wheelNormal)
    {
        if (vehicle != null && vehicle.SteeringPivot != null)
        {
            float turn = Mathf.Clamp(-vehicle.SteerAngle * vehicle.Profile.SteeringRatio, -HandFollowLimit, HandFollowLimit);
            Quaternion frame = wheelFrame * Quaternion.Euler(0f, 0f, turn);
            // Quarter to three: the rim at about 20 degrees above the horizontal on each side, on the driver's face.
            Vector3 l = new Vector3(-wheelRadius * 0.94f, wheelRadius * 0.34f, -0.02f);
            Vector3 r = new Vector3(wheelRadius * 0.94f, wheelRadius * 0.34f, -0.02f);
            left = car.TransformPoint(wheelCentre + frame * l);
            right = car.TransformPoint(wheelCentre + frame * r);
            wheelNormal = car.TransformDirection(wheelFrame * Vector3.forward);
            return;
        }
        left = car.TransformPoint(staticLeftGrip);
        right = car.TransformPoint(staticRightGrip);
        wheelNormal = car.forward;
    }

    private void LateUpdate()
    {
        if (vehicle != null && vehicle.SteeringPivot != null) Arms();
    }

    // Two-bone solve (as the cyclists use): the end joint lands on the target with the middle joint bending toward the
    // pole; with squareHinge the upper bone also turns about its length so the knee folds as a hinge.
    private static Vector3 SolveLimb(Transform upperBone, Transform lowerBone, Transform end, Vector3 target, Vector3 pole, bool squareHinge)
    {
        Vector3 root = upperBone.position, toTarget = target - root;
        float upper = Vector3.Distance(root, lowerBone.position), lower = Vector3.Distance(lowerBone.position, end.position);
        if (upper < 0.0001f || lower < 0.0001f || toTarget.sqrMagnitude < 0.000001f) return end.position;
        Vector3 axis = toTarget.normalized;
        float reach = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upper - lower) + 0.001f, (upper + lower) * 0.999f);
        float cos = Mathf.Clamp((upper * upper + reach * reach - lower * lower) / (2f * upper * reach), -1f, 1f);
        Vector3 bend = Vector3.ProjectOnPlane(pole, axis).normalized;
        Vector3 middle = root + upper * (cos * axis + Mathf.Sqrt(1f - cos * cos) * bend);
        upperBone.rotation = Quaternion.FromToRotation(lowerBone.position - root, middle - root) * upperBone.rotation;
        if (squareHinge)
        {
            Vector3 hinge = Vector3.ProjectOnPlane(upperBone.right, middle - root), normal = Vector3.Cross(axis, bend);
            upperBone.rotation = Quaternion.FromToRotation(hinge, Vector3.Dot(hinge, normal) < 0f ? -normal : normal) * upperBone.rotation;
        }
        Vector3 tip = root + axis * reach;
        lowerBone.rotation = Quaternion.FromToRotation(end.position - lowerBone.position, tip - lowerBone.position) * lowerBone.rotation;
        return tip;
    }
}
