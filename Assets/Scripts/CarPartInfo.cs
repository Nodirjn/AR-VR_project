using UnityEngine;

/// <summary>Where a node sits in the hero car's structure tree.</summary>
public enum CarNodeKind
{
    /// <summary>The whole car (the tree's root).</summary>
    Vehicle,
    /// <summary>A category such as Brakes or Steering. Shown in the tree; never moves as a unit.</summary>
    System,
    /// <summary>A unit that comes out of the car first (an engine, a suspension corner, a door).</summary>
    Assembly,
    /// <summary>A group inside an assembly that comes apart one level at a time.</summary>
    SubAssembly,
    /// <summary>A single part (a leaf of the tree).</summary>
    Part
}

/// <summary>
/// Engineering metadata on a node of the hero car, read by the Car Explorer's structure tree: what the node is called,
/// what it does, what it is made of and how it comes apart. Nodes with this component define the tree under them;
/// nodes without it are grouped by the explorer's own rules.
/// </summary>
[DisallowMultipleComponent]
public sealed class CarPartInfo : MonoBehaviour
{
    [Tooltip("Assembly, SubAssembly or Part. Systems are named by the system field, not by nodes.")]
    public CarNodeKind kind = CarNodeKind.Part;
    [Tooltip("Stable id, unique in the car, lower case with dots, e.g. 'suspension.front-left.lower-arm'.")]
    public string id;
    public string nameEnglish;
    public string nameUzbek;
    [TextArea] public string descriptionEnglish;
    [TextArea] public string descriptionUzbek;
    [Tooltip("English system name, one of CarSystems.English.")]
    public string system;
    [Tooltip("Main material in English, e.g. 'Forged aluminium'. Empty when not meaningful.")]
    public string material;
    [Tooltip("Direction (in the parent's local space) in which this node moves away from its siblings when its " +
             "parent comes apart. Zero lets the explorer choose.")]
    public Vector3 explodeDirection;
    [Tooltip("How far this node moves when its parent comes apart, as a multiple of the explorer's default.")]
    public float explodeDistance = 1f;
    [Tooltip("Small detail (fasteners, clips, thin wires) that the Low quality level (Quest) does not draw.")]
    public bool fineDetail;
}

/// <summary>The systems of the structure tree, in display order, with their Uzbek names.</summary>
public static class CarSystems
{
    public static readonly string[] English =
    {
        "Body", "Structure", "Closures", "Interior", "Wheels", "Brakes", "Suspension", "Steering", "Powertrain",
        "Driveline", "Exhaust", "Fuel", "Cooling", "Electrical"
    };

    public static readonly string[] Uzbek =
    {
        "Kuzov", "Kuzov karkasi", "Eshik va qopqoqlar", "Salon", "G‘ildiraklar", "Tormoz tizimi", "Osma",
        "Rul boshqaruvi", "Kuch agregati", "Transmissiya", "Chiqarish tizimi", "Yoqilg‘i tizimi",
        "Sovutish tizimi", "Elektr tizimi"
    };

    /// <summary>The Uzbek name of an English system name, or the English name when it is not in the list.</summary>
    public static string ToUzbek(string english)
    {
        int index = System.Array.IndexOf(English, english);
        return index >= 0 ? Uzbek[index] : english;
    }

    /// <summary>Display order of a system (unknown systems go last).</summary>
    public static int Order(string english)
    {
        int index = System.Array.IndexOf(English, english);
        return index >= 0 ? index : English.Length;
    }
}
