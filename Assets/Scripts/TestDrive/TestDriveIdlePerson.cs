using UnityEngine;

/// <summary>
/// A person who is not walking: standing in a group and talking, or sitting on a bench. Posed from the bind pose with
/// the same two-bone solve the drivers and cyclists use, then kept alive cheaply in LateUpdate: breathing, weight
/// shifts, turning the head to whoever is speaking, nods, and the speaker's hand gestures. Speakers take turns.
/// </summary>
public sealed class TestDriveIdlePerson : MonoBehaviour
{
    public enum Mode { Stand, Sit }

    private Mode mode;
    private Transform hips, spine, chest, neck, head;
    private Transform lUpper, lLower, lHand, rUpper, rLower, rHand, lThigh, lShin, lFoot, rThigh, rShin, rFoot;
    private Quaternion spineBase, neckBase, lUpperBase, lLowerBase, rUpperBase, rLowerBase, lHandBase, rHandBase;
    private Vector3 hipsBase;
    private Transform[] group;
    private int index;
    private float seed;
    private bool posed, working;

    public Transform Head => head;

    /// <summary>Copies a walker (without its walking script) and stands or seats it at a world pose.</summary>
    public static TestDriveIdlePerson Create(GameObject walker, Transform parent, Vector3 position, Quaternion facing, Mode mode, float seed)
    {
        var holderObject = new GameObject("Idle Person Staging");
        holderObject.SetActive(false);
        GameObject copy = Object.Instantiate(walker, holderObject.transform);
        foreach (MonoBehaviour b in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(b);
        foreach (CharacterController c in copy.GetComponentsInChildren<CharacterController>(true)) Object.DestroyImmediate(c);
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Rigidbody r in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(r);
        copy.name = mode == Mode.Sit ? "Sitting Person" : "Talking Person";
        copy.transform.SetParent(parent, true);
        copy.transform.SetPositionAndRotation(position, facing);
        Object.Destroy(holderObject);
        TestDriveIdlePerson person = copy.AddComponent<TestDriveIdlePerson>();
        person.mode = mode;
        person.seed = seed;
        if (!person.Bind()) { Object.Destroy(copy); return null; }
        return person;
    }

    public void SetGroup(Transform[] heads, int self)
    {
        group = heads;
        index = self;
    }

    private bool Bind()
    {
        Animator animator = GetComponentInChildren<Animator>(true);
        Animator human = null;
        if (animator != null) { animator.enabled = true; if (animator.isHuman) human = animator; }
        hips = Bone(human, HumanBodyBones.Hips, "Hips");
        spine = Bone(human, HumanBodyBones.Spine, "Spine");
        chest = Bone(human, HumanBodyBones.Chest, "Spine1");
        neck = Bone(human, HumanBodyBones.Neck, "Neck");
        head = Bone(human, HumanBodyBones.Head, "Head");
        lUpper = Bone(human, HumanBodyBones.LeftUpperArm, "LeftArm");
        lLower = Bone(human, HumanBodyBones.LeftLowerArm, "LeftForeArm");
        lHand = Bone(human, HumanBodyBones.LeftHand, "LeftHand");
        rUpper = Bone(human, HumanBodyBones.RightUpperArm, "RightArm");
        rLower = Bone(human, HumanBodyBones.RightLowerArm, "RightForeArm");
        rHand = Bone(human, HumanBodyBones.RightHand, "RightHand");
        lThigh = Bone(human, HumanBodyBones.LeftUpperLeg, "LeftUpLeg");
        lShin = Bone(human, HumanBodyBones.LeftLowerLeg, "LeftLeg");
        lFoot = Bone(human, HumanBodyBones.LeftFoot, "LeftFoot");
        rThigh = Bone(human, HumanBodyBones.RightUpperLeg, "RightUpLeg");
        rShin = Bone(human, HumanBodyBones.RightLowerLeg, "RightLeg");
        rFoot = Bone(human, HumanBodyBones.RightFoot, "RightFoot");
        if (animator != null) animator.enabled = false;
        foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Transform bone = skin.rootBone != null ? skin.rootBone : skin.transform;
            skin.localBounds = new Bounds(Vector3.zero, Vector3.one * (2.5f / Mathf.Max(0.0001f, bone.lossyScale.x)));
        }
        return hips != null && spine != null && neck != null && head != null && lUpper != null && lLower != null && lHand != null &&
               rUpper != null && rLower != null && rHand != null && lThigh != null && lShin != null && lFoot != null &&
               rThigh != null && rShin != null && rFoot != null;
    }

    private Transform Bone(Animator human, HumanBodyBones bone, string mixamoName)
    {
        Transform found = human != null ? human.GetBoneTransform(bone) : null;
        if (found != null) return found;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == mixamoName || t.name.EndsWith(":" + mixamoName, System.StringComparison.Ordinal)) return t;
        return null;
    }

    /// <summary>Sits the hips on <paramref name="seat"/> (world): thighs along the seat, shins down to the ground,
    /// hands resting on the thighs.</summary>
    public void Sit(Vector3 seat, float groundY)
    {
        Vector3 right = transform.right, up = Vector3.up, forward = transform.forward;
        transform.position += seat - hips.position;
        float thigh = Vector3.Distance(lThigh.position, lShin.position), shin = Vector3.Distance(lShin.position, lFoot.position);
        LegTo(lThigh, lShin, lFoot, -1f, thigh, shin, groundY, forward, up, right);
        LegTo(rThigh, rShin, rFoot, 1f, thigh, shin, groundY, forward, up, right);
        spine.rotation = Quaternion.FromToRotation(neck.position - spine.position, Quaternion.AngleAxis(-8f, right) * up) * spine.rotation;
        Vector3 lKnee = lShin.position, rKnee = rShin.position;
        SolveLimb(lUpper, lLower, lHand, Vector3.Lerp(lThigh.position, lKnee, 0.55f) + up * 0.1f, -up - right * 0.5f + forward * 0.2f);
        SolveLimb(rUpper, rLower, rHand, Vector3.Lerp(rThigh.position, rKnee, 0.55f) + up * 0.1f, -up + right * 0.5f + forward * 0.2f);
        Finish();
    }

    private void LegTo(Transform t, Transform s, Transform f, float side, float thigh, float shin, float groundY,
        Vector3 forward, Vector3 up, Vector3 right)
    {
        Vector3 hip = t.position;
        float drop = Mathf.Max(0.05f, hip.y - (groundY + 0.08f));
        Vector3 knee = hip + forward * (thigh * 0.95f) + right * (side * 0.06f);
        float down = Mathf.Min(drop - (knee.y - hip.y), shin * 0.98f);
        Vector3 ankle = knee + Vector3.down * down + forward * 0.08f;
        SolveLimb(t, s, f, ankle, forward + up, true);
    }

    /// <summary>Seated at a desk: forearms forward, hands on the keyboard at <paramref name="keyboard"/> (world), head
    /// down a little to the screen. Call after <see cref="Sit"/>.</summary>
    public void Work(Vector3 keyboard)
    {
        Vector3 right = transform.right, forward = transform.forward;
        SolveLimb(lUpper, lLower, lHand, keyboard - right * 0.13f, -Vector3.up - right * 0.8f - forward * 0.3f);
        SolveLimb(rUpper, rLower, rHand, keyboard + right * 0.13f, -Vector3.up + right * 0.8f - forward * 0.3f);
        neck.rotation = Quaternion.AngleAxis(8f, right) * neck.rotation;
        working = true;
        Finish();
    }

    /// <summary>Standing: arms down by the sides, slightly bent.</summary>
    public void Stand()
    {
        Vector3 right = transform.right, forward = transform.forward;
        float arm = Vector3.Distance(lUpper.position, lLower.position) + Vector3.Distance(lLower.position, lHand.position);
        SolveLimb(lUpper, lLower, lHand, lUpper.position + Vector3.down * (arm * 0.93f) + forward * 0.06f - right * 0.08f, -forward - right * 0.3f);
        SolveLimb(rUpper, rLower, rHand, rUpper.position + Vector3.down * (arm * 0.93f) + forward * 0.06f + right * 0.08f, -forward + right * 0.3f);
        Finish();
    }

    private void Finish()
    {
        spineBase = spine.localRotation;
        neckBase = neck.localRotation;
        lUpperBase = lUpper.localRotation; lLowerBase = lLower.localRotation; lHandBase = lHand.localRotation;
        rUpperBase = rUpper.localRotation; rLowerBase = rLower.localRotation; rHandBase = rHand.localRotation;
        hipsBase = hips.localPosition;
        posed = true;
    }

    private void LateUpdate()
    {
        if (!posed) return;
        float t = Time.time + seed * 13.7f;
        // Breathing and a slow sway.
        spine.localRotation = spineBase * Quaternion.Euler(Mathf.Sin(t * 1.6f) * 0.8f, Mathf.Sin(t * 0.23f) * 2f, Mathf.Sin(t * 0.31f) * 1.2f);
        if (mode == Mode.Stand) hips.localPosition = hipsBase + new Vector3(Mathf.Sin(t * 0.29f) * 0.015f, 0f, 0f) / Mathf.Max(1e-4f, transform.lossyScale.x);

        // Conversation: a speaker every few seconds (by the clock, so the group agrees without talking to itself).
        int count = group != null ? group.Length : 1;
        int turn = Mathf.FloorToInt((Time.time + seed * 0.5f) / 4.5f);
        int speaker = count > 0 ? Mathf.Abs(turn * 7 + 3) % count : 0;
        bool speaking = group != null && speaker == index;

        // Head towards the speaker (or the next person when speaking), nodding now and then.
        neck.localRotation = neckBase;
        if (group != null && count > 1)
        {
            Transform target = group[speaking ? (index + 1) % count : speaker];
            if (target != null && target != head)
            {
                Vector3 to = Vector3.ProjectOnPlane(target.position - head.position, Vector3.up);
                float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), to, Vector3.up);
                yaw = Mathf.Clamp(yaw, -55f, 55f);
                float nod = speaking ? Mathf.Sin(t * 3.1f) * 3f : Mathf.Max(0f, Mathf.Sin(t * 1.3f)) * 6f;
                neck.rotation = Quaternion.AngleAxis(yaw * 0.85f, Vector3.up) * Quaternion.AngleAxis(nod, transform.right) * neck.rotation;
            }
        }

        // The speaker's hands talk.
        lUpper.localRotation = lUpperBase; lLower.localRotation = lLowerBase; lHand.localRotation = lHandBase;
        rUpper.localRotation = rUpperBase; rLower.localRotation = rLowerBase; rHand.localRotation = rHandBase;
        if (working)
        {
            // Typing: small quick hand movements, a pause now and then to read the screen.
            float typing = Mathf.Clamp01(Mathf.Sin(t * 0.4f) * 2f + 0.6f);
            lHand.localRotation = lHandBase * Quaternion.Euler(Mathf.Sin(t * 11f) * 4f * typing, 0f, Mathf.Sin(t * 7f) * 2f * typing);
            rHand.localRotation = rHandBase * Quaternion.Euler(Mathf.Sin(t * 9.3f + 1f) * 4f * typing, 0f, Mathf.Sin(t * 6.1f) * 2f * typing);
            neck.localRotation = neckBase * Quaternion.Euler(Mathf.Sin(t * 0.17f) * 3f, Mathf.Sin(t * 0.11f) * 10f * (1f - typing), 0f);
        }
        if (speaking && mode == Mode.Stand)
        {
            float phase = ((Time.time + seed * 0.5f) % 4.5f) / 4.5f;
            float raise = Mathf.Sin(phase * Mathf.PI);
            Vector3 right = transform.right, forward = transform.forward;
            Vector3 chestPoint = (chest != null ? chest.position : spine.position) + forward * 0.32f;
            Vector3 hand = chestPoint + right * (0.12f + Mathf.Sin(t * 2.3f) * 0.06f) + Vector3.up * (Mathf.Sin(t * 3.7f) * 0.05f - 0.08f);
            Vector3 rest = rHand.position;
            SolveLimb(rUpper, rLower, rHand, Vector3.Lerp(rest, hand, raise), -Vector3.up + right * 0.6f - forward * 0.2f);
            if (seed > 0.5f)
            {
                Vector3 other = chestPoint - right * (0.1f + Mathf.Sin(t * 1.9f) * 0.05f) - Vector3.up * 0.12f;
                SolveLimb(lUpper, lLower, lHand, Vector3.Lerp(lHand.position, other, raise * 0.7f), -Vector3.up - right * 0.6f - forward * 0.2f);
            }
        }
    }

    private static Vector3 SolveLimb(Transform upperBone, Transform lowerBone, Transform end, Vector3 target, Vector3 pole, bool squareHinge = false)
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
