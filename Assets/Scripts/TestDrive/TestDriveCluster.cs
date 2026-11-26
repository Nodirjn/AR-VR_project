using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A live display laid over the car's own instrument cluster, so the cluster behind the steering wheel shows the real
/// selector (P R N D), gear and speed instead of the model's painted "P". The surface is found once, by casting rays
/// from the driver's eye past the top of the steering wheel onto the cabin's triangles; the panel is depth tested,
/// so the wheel rim and the driver's hands still pass in front of it.
/// </summary>
public sealed class TestDriveCluster : MonoBehaviour
{
    private TestDriveVehicle vehicle;
    private TextMeshProUGUI speed, gear;
    private readonly TextMeshProUGUI[] letters = new TextMeshProUGUI[4];
    private Image revFill;
    private string lastSelector;
    private int lastSpeed = -1, lastGear = -1;

    public static TestDriveCluster Create(Transform root, TestDriveVehicle vehicle, List<MeshRenderer> bodyRenderers)
    {
        if (vehicle.SteeringPivot == null || vehicle.EyeAnchor == null) return null;
        Vector3 eye = vehicle.EyeAnchor.localPosition;
        Vector3 hub = vehicle.SteeringPivot.localPosition;
        Vector3 column = vehicle.SteeringPivot.localRotation * Vector3.forward;
        Matrix4x4 rootFromWorld = root.worldToLocalMatrix;

        // Candidate points just above the hub, a little beyond the wheel along the column.
        float[] lifts = { 0.07f, 0.1f, 0.13f, 0.16f };
        Vector3 bestPoint = Vector3.zero, bestNormal = Vector3.zero;
        float bestScore = float.MinValue;
        for (int k = 0; k < lifts.Length; k++)
        {
            Vector3 target = hub + column * 0.18f + Vector3.up * lifts[k];
            Vector3 direction = (target - eye).normalized;
            float reach = (target - eye).magnitude;
            var ray = new Ray(eye, direction);
            float best = reach + 0.4f;
            Vector3 normal = Vector3.zero;
            bool hit = false;
            for (int r = 0; r < bodyRenderers.Count; r++)
            {
                MeshRenderer renderer = bodyRenderers[r];
                if (renderer == null || renderer.transform.IsChildOf(vehicle.SteeringPivot)) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Matrix4x4 toCar = rootFromWorld * renderer.transform.localToWorldMatrix;
                Bounds box = TestDriveWheelRig.CarBox(toCar, filter.sharedMesh.bounds);
                if (!box.IntersectRay(ray)) continue;
                hit |= TestDriveMeshCut.Raycast(filter.sharedMesh, toCar, ray, 0.3f, ref best, ref normal);
            }
            if (!hit) continue;
            // Prefer a surface that faces the driver and lies past the wheel (the dial face, not the hood's lip).
            float facing = Mathf.Abs(Vector3.Dot(normal, direction));
            if (facing < 0.35f || best < reach - 0.12f) continue;
            float score = facing + best * 0.5f;
            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = eye + direction * best;
                bestNormal = Vector3.Dot(normal, direction) > 0f ? -normal : normal;
            }
        }
        if (bestScore == float.MinValue)
        {
            Debug.Log("[Test Drive] " + vehicle.Profile.DisplayName + ": no instrument cluster surface found; the head-up display shows the gear.");
            return null;
        }

        var holder = new GameObject("Instrument Cluster Display").transform;
        holder.SetParent(root, false);
        Vector3 toEye = (eye - bestPoint).normalized;
        holder.localPosition = bestPoint + toEye * 0.008f;
        // Lie on the dial face, turned a little towards the eye so it stays legible.
        Vector3 face = Vector3.Slerp(-bestNormal, -toEye, 0.5f).normalized;
        holder.localRotation = Quaternion.LookRotation(face, Vector3.up);
        TestDriveCluster cluster = holder.gameObject.AddComponent<TestDriveCluster>();
        cluster.vehicle = vehicle;
        cluster.Build(holder);
        return cluster;
    }

    private void Build(Transform holder)
    {
        RectTransform canvas = TestDriveUi.WorldCanvas("Cluster Canvas", holder, new Vector2(340f, 120f), 0.00042f, false, 10);
        canvas.localRotation = Quaternion.identity;
        Image back = TestDriveUi.Rounded(canvas, "Screen", 18, new Color(0.012f, 0.016f, 0.024f, 0.96f));
        CarExplorerHudStyle.Stretch(back.rectTransform);

        string[] names = { "P", "R", "N", "D" };
        for (int i = 0; i < 4; i++)
        {
            letters[i] = TestDriveUi.Label(canvas, "Selector " + names[i], names[i], 26f, TestDriveUi.HudDim, TextAlignmentOptions.Center, FontStyles.Bold);
            TestDriveUi.Place(letters[i].rectTransform, 14f + i * 30f, 10f, 30f, 32f);
        }
        gear = TestDriveUi.Label(canvas, "Gear", "P", 64f, TestDriveUi.HudWhite, TextAlignmentOptions.Center, FontStyles.Bold);
        TestDriveUi.Place(gear.rectTransform, 14f, 40f, 120f, 70f);

        speed = TestDriveUi.Label(canvas, "Speed", "0", 70f, TestDriveUi.HudWhite, TextAlignmentOptions.Right, FontStyles.Bold);
        TestDriveUi.Place(speed.rectTransform, 140f, 12f, 150f, 76f);
        TextMeshProUGUI unit = TestDriveUi.Label(canvas, "Unit", "km/h", 18f, TestDriveUi.HudDim, TextAlignmentOptions.Right);
        TestDriveUi.Place(unit.rectTransform, 140f, 84f, 150f, 22f);

        Image revBack = TestDriveUi.Rounded(canvas, "Rev Track", 3, new Color(1f, 1f, 1f, 0.12f));
        TestDriveUi.Place(revBack.rectTransform, 300f, 14f, 10f, 92f);
        revFill = TestDriveUi.Rounded(revBack.transform, "Rev Fill", 3, TestDriveUi.HudCyan);
        revFill.rectTransform.anchorMin = new Vector2(0f, 0f);
        revFill.rectTransform.anchorMax = new Vector2(1f, 0f);
        revFill.rectTransform.pivot = new Vector2(0.5f, 0f);
        revFill.rectTransform.anchoredPosition = Vector2.zero;
        revFill.rectTransform.sizeDelta = new Vector2(0f, 0f);
    }

    private void LateUpdate()
    {
        if (vehicle == null) return;
        string selector = vehicle.SelectorLetter;
        int shownGear = vehicle.CurrentSelector == TestDriveVehicle.Selector.D && !vehicle.Profile.IsElectric ? vehicle.Gear + 1 : 0;
        if (selector != lastSelector || shownGear != lastGear)
        {
            lastSelector = selector;
            lastGear = shownGear;
            for (int i = 0; i < 4; i++)
            {
                bool active = letters[i].text == selector;
                letters[i].color = active ? (selector == "R" ? TestDriveUi.HudAmber : TestDriveUi.HudWhite) : new Color(0.5f, 0.56f, 0.62f, 0.55f);
            }
            gear.text = shownGear > 0 ? "D" + shownGear : selector;
            gear.color = selector == "R" ? TestDriveUi.HudAmber : TestDriveUi.HudWhite;
        }
        int kmh = Mathf.RoundToInt(vehicle.SpeedKmh);
        if (kmh != lastSpeed)
        {
            lastSpeed = kmh;
            speed.text = kmh.ToString();
        }
        float fraction;
        if (vehicle.Profile.IsElectric) fraction = Mathf.Clamp01(vehicle.Throttle);
        else fraction = vehicle.EngineRunning ? Mathf.Clamp01(vehicle.Rpm / vehicle.Profile.RedlineRpm) : 0f;
        revFill.rectTransform.sizeDelta = new Vector2(0f, 92f * fraction);
        revFill.color = fraction > 0.88f && !vehicle.Profile.IsElectric ? TestDriveUi.HudRed : TestDriveUi.HudCyan;
    }
}
