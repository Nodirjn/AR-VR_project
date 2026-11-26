using UnityEngine;

/// <summary>
/// A natural walk for the city's pedestrians, layered over their own movement. Feet are placed by a stride cycle
/// tied to the distance actually walked (60 % stance with the foot travelling back flat on the ground, 40 % swing
/// with the heel lifting and the toe clearing the ground), legs solved to them with the knee forward; the pelvis
/// rises over the standing leg, sways a little to it and turns with the stride; the chest turns against it; the
/// arms swing opposite the legs with a relaxed elbow; the head steadies. Standing still, the weight settles evenly.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class TestDriveGait : MonoBehaviour
{
    private Transform hips, spine, chest, neck, lThigh, lShin, lFoot, rThigh, rShin, rFoot, lUpper, lLower, lHand, rUpper, rLower, rHand;
    private Quaternion hipsRestRotation, spineRest, chestRest, neckRest, lFootRest, rFootRest;
    private Vector3 hipsRest;
    private float leg, arm, ankleHeight, phase, speedSmooth, bound;
    private Vector3 lastPosition;
    private bool ready;
    private float scale;

    public static void Install()
    {
        foreach (ExteriorPedestrianAgent w in Object.FindObjectsByType<ExteriorPedestrianAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (w.GetComponent<TestDriveGait>() == null) w.gameObject.AddComponent<TestDriveGait>();
    }

    private Transform Bone(Animator human, HumanBodyBones bone, string mixamo)
    {
        Transform t = human != null ? human.GetBoneTransform(bone) : null;
        if (t != null) return t;
        foreach (Transform c in GetComponentsInChildren<Transform>(true))
            if (c.name == mixamo || c.name.EndsWith(":" + mixamo, System.StringComparison.Ordinal)) return c;
        return null;
    }

    private void Start()
    {
        Animator a = GetComponent<Animator>();
        Animator human = a != null && a.isHuman ? a : null;
        hips = Bone(human, HumanBodyBones.Hips, "Hips");
        spine = Bone(human, HumanBodyBones.Spine, "Spine");
        chest = Bone(human, HumanBodyBones.Chest, "Spine1");
        neck = Bone(human, HumanBodyBones.Neck, "Neck");
        lThigh = Bone(human, HumanBodyBones.LeftUpperLeg, "LeftUpLeg"); lShin = Bone(human, HumanBodyBones.LeftLowerLeg, "LeftLeg"); lFoot = Bone(human, HumanBodyBones.LeftFoot, "LeftFoot");
        rThigh = Bone(human, HumanBodyBones.RightUpperLeg, "RightUpLeg"); rShin = Bone(human, HumanBodyBones.RightLowerLeg, "RightLeg"); rFoot = Bone(human, HumanBodyBones.RightFoot, "RightFoot");
        lUpper = Bone(human, HumanBodyBones.LeftUpperArm, "LeftArm"); lLower = Bone(human, HumanBodyBones.LeftLowerArm, "LeftForeArm"); lHand = Bone(human, HumanBodyBones.LeftHand, "LeftHand");
        rUpper = Bone(human, HumanBodyBones.RightUpperArm, "RightArm"); rLower = Bone(human, HumanBodyBones.RightLowerArm, "RightForeArm"); rHand = Bone(human, HumanBodyBones.RightHand, "RightHand");
        ready = hips && spine && lThigh && lShin && lFoot && rThigh && rShin && rFoot && lUpper && lLower && lHand && rUpper && rLower && rHand;
        if (!ready) { enabled = false; return; }
        leg = Vector3.Distance(lThigh.position, lShin.position) + Vector3.Distance(lShin.position, lFoot.position);
        arm = Vector3.Distance(lUpper.position, lLower.position) + Vector3.Distance(lLower.position, lHand.position);
        // The ankles' height above the walker's own origin in the rest pose (wherever the model's pivot is).
        ankleHeight = (lFoot.position.y + rFoot.position.y) * 0.5f - transform.position.y;
        hipsRest = hips.localPosition;
        hipsRestRotation = hips.localRotation;
        spineRest = spine.localRotation;
        chestRest = chest != null ? chest.localRotation : Quaternion.identity;
        neckRest = neck != null ? neck.localRotation : Quaternion.identity;
        lFootRest = lFoot.localRotation;
        rFootRest = rFoot.localRotation;
        lastPosition = transform.position;
        scale = Mathf.Max(1e-4f, transform.lossyScale.y);
        phase = Random.value;
    }

    private void LateUpdate()
    {
        if (!ready) return;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 moved = transform.position - lastPosition;
        moved.y = 0f;
        lastPosition = transform.position;
        float speed = moved.magnitude / dt;
        if (speed > 4f) speed = 0f;                         // a teleport, not a walk
        speedSmooth = Mathf.Lerp(speedSmooth, speed, 1f - Mathf.Exp(-8f * dt));
        float walk = Mathf.Clamp01(speedSmooth / 0.6f);     // 0 standing, 1 walking

        // Stride: step length grows with speed (a comfortable 0.65-0.8 m for an adult).
        float step = leg * Mathf.Lerp(0.62f, 0.85f, Mathf.InverseLerp(0.8f, 1.6f, speedSmooth));
        phase += moved.magnitude / (2f * step);
        float p = phase - Mathf.Floor(phase);

        Vector3 up = Vector3.up, forward = transform.forward, right = transform.right;
        forward.y = 0f; forward.Normalize();
        right.y = 0f; right.Normalize();

        // Pelvis: highest over the standing leg (twice per stride), shifted a little over it, turned with the swing.
        float bob = Mathf.Cos(p * Mathf.PI * 4f) * 0.022f * walk;
        float sway = Mathf.Sin(p * Mathf.PI * 2f) * 0.025f * walk;
        hips.localPosition = hipsRest;
        hips.localRotation = hipsRestRotation;
        hips.position += up * (bob - 0.012f * walk) + right * sway;
        float twist = Mathf.Sin(p * Mathf.PI * 2f) * 5f * walk;
        hips.rotation = Quaternion.AngleAxis(twist, up) * hips.rotation;
        spine.localRotation = spineRest;
        spine.rotation = Quaternion.AngleAxis(-twist * 1.6f, up) * Quaternion.AngleAxis(3f * walk, right) * spine.rotation;
        if (neck != null)
        {
            neck.localRotation = neckRest;
            neck.rotation = Quaternion.AngleAxis(-twist * 0.4f, up) * Quaternion.AngleAxis(-bob * 40f, right) * neck.rotation;
        }

        Foot(lThigh, lShin, lFoot, lFootRest, -1f, p, step, walk, forward, right, up);
        Foot(rThigh, rShin, rFoot, rFootRest, 1f, Mathf.Repeat(p + 0.5f, 1f), step, walk, forward, right, up);
        // Arms swing against the legs: the left arm forward with the right leg.
        Arm(lUpper, lLower, lHand, -1f, Mathf.Repeat(p + 0.5f, 1f), walk, forward, right, up);
        Arm(rUpper, rLower, rHand, 1f, p, walk, forward, right, up);
    }

    /// <summary>Where the foot is in its cycle: stance 0-0.6 (on the ground, sliding back under the body), swing 0.6-1.</summary>
    private void Foot(Transform thigh, Transform shin, Transform foot, Quaternion rest, float side, float p, float step, float walk,
        Vector3 forward, Vector3 right, Vector3 up)
    {
        Vector3 hip = thigh.position;
        float ground = transform.position.y;
        float along, lift, pitch;
        if (p < 0.6f)
        {
            float s = p / 0.6f;
            along = Mathf.Lerp(0.5f, -0.5f, s) * step;
            lift = 0f;
            pitch = Mathf.Lerp(-12f, 18f, s) * (s > 0.75f ? 1f : 0.4f);   // heel strike, flat, heel rising
        }
        else
        {
            float s = (p - 0.6f) / 0.4f;
            float e = Mathf.SmoothStep(0f, 1f, s);
            along = Mathf.Lerp(-0.5f, 0.5f, e) * step;
            lift = Mathf.Sin(s * Mathf.PI) * 0.11f * step / 0.7f;
            pitch = Mathf.Lerp(25f, -12f, e);
        }
        along *= walk;
        lift *= walk;
        pitch *= walk;
        Vector3 under = new Vector3(hip.x, ground, hip.z);
        Vector3 ankle = under + forward * along + right * (side * 0.03f) + up * (ankleHeight + lift);
        float reach = Vector3.Distance(hip, ankle);
        if (reach > leg * 0.995f) ankle = hip + (ankle - hip).normalized * leg * 0.995f;
        SolveLimb(thigh, shin, foot, ankle, forward + up * 0.2f + right * (side * 0.08f), true);
        foot.localRotation = rest;
        foot.rotation = Quaternion.AngleAxis(pitch, right) * foot.rotation;
    }

    private void Arm(Transform upper, Transform lower, Transform hand, float side, float p, float walk, Vector3 forward, Vector3 right, Vector3 up)
    {
        float swing = Mathf.Sin(p * Mathf.PI * 2f + Mathf.PI * 0.5f) * walk;    // in step with the opposite leg
        Vector3 shoulder = upper.position;
        Vector3 target = shoulder + Vector3.down * (arm * 0.9f) + forward * (0.04f + swing * 0.2f * arm / 0.55f) + right * (side * 0.09f);
        // Elbow bends more on the forward swing.
        SolveLimb(upper, lower, hand, target, -forward * (1f - Mathf.Max(0f, swing) * 0.5f) + right * (side * 0.35f), false);
    }

    private static void SolveLimb(Transform upperBone, Transform lowerBone, Transform end, Vector3 target, Vector3 pole, bool squareHinge)
    {
        Vector3 root = upperBone.position, toTarget = target - root;
        float upper = Vector3.Distance(root, lowerBone.position), lower = Vector3.Distance(lowerBone.position, end.position);
        if (upper < 0.0001f || lower < 0.0001f || toTarget.sqrMagnitude < 0.000001f) return;
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
    }
}
