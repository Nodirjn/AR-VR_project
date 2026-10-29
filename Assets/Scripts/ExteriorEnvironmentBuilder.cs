using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

/// <summary>Editor-time builder for the scene's modular Aurelle dealership district.</summary>
[DisallowMultipleComponent]
public sealed class ExteriorEnvironmentBuilder : MonoBehaviour
{
#if UNITY_EDITOR
    private const string ModelFolder = "Assets/Models/Exterior";
    private const string MaterialFolder = "Assets/Materials/Exterior";
    private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    private Material asphalt, asphaltVertical, concrete, paving, facadeCream, facadeWarm, facadeGraphite, facadeBlue;
    private Material glass, metal, gold, foliage, foliageLight, trunk, roadLine, bikeLane, signFace;
    private Material redOn, redOff, amberOn, amberOff, greenOn, greenOff, walkOn, walkOff;
    private Material tire, wheelMetal, headlight, tailLight, carBlue, carSilver, carRed, carWhite;
    private GameObject[] trafficVehiclePrefabs;
    private Mesh groundMesh, hillMesh, sedanBodyMesh, sedanCabinMesh;
    private readonly List<Vector3> plantedTrees = new List<Vector3>();
    // World footprints of the buildings' facade masses; curbs, sidewalks, trees and lamps keep clear of them.
    private readonly List<Rect> buildingFootprints = new List<Rect>();
    private static readonly float[] EastWestStreets = { -47f, 60f, 118f };
    private static readonly float[] NorthSouthStreets = { -96f, 96f };
    // North-south streets end 6.5 m past the outer junctions, clear of the buildings facing their ends.
    private const float NorthSouthStart = -60f, NorthSouthEnd = 131f;
    // Paved areas besides the streets: arrival apron, customer parking and its driveway, cycle tracks.
    private static readonly Rect[] PavedAreas =
    {
        Rect.MinMaxRect(-23f, 18f, 25f, 53.6f), Rect.MinMaxRect(-64.5f, -7.5f, -37.5f, 37.5f), Rect.MinMaxRect(-54f, 47.3f, -46f, 60.3f),
        Rect.MinMaxRect(-140f, 74.1f, 140f, 75.9f), Rect.MinMaxRect(-128.9f, 54.4f, -127.1f, 75f), Rect.MinMaxRect(127.1f, 54.4f, 128.9f, 75f)
    };
    // The existing Showroom Floor (50 x 50 m plane at y = -0.05). The city ground drops below it so the two never z-fight.
    private static readonly Rect ShowroomFloor = Rect.MinMaxRect(-25f, -25f, 25f, 25f);
    private const float ShowroomGroundY = -.2f;

    [ContextMenu("Build Aurelle Exterior District")]
    public void BuildEnvironment()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Stop Play Mode before building the exterior district.", this);
            return;
        }
        Scene scene = gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
        {
            Debug.LogWarning("Build the exterior district from an open scene.", this);
            return;
        }
        // Daylight settings apply to the active scene, so the builder's own scene is active while it builds.
        Scene previousActive = SceneManager.GetActiveScene();
        if (scene != previousActive) SceneManager.SetActiveScene(scene);
        try
        {
            BuildDistrict();
        }
        finally
        {
            if (scene != previousActive && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Aurelle exterior district built: connected roads, arrival plaza, neighborhood blocks, traffic, pedestrians, cyclists and spawn point.", this);
    }

    private void BuildDistrict()
    {
        EnsureFolder("Assets/Materials");
        EnsureFolder(MaterialFolder);
        EnsureFolder(MaterialFolder + "/Textures");
        EnsureFolder("Assets/Models");
        EnsureFolder(ModelFolder);
        EnsureFolder("Assets/Prefabs");
        EnsureFolder("Assets/Prefabs/ExteriorTraffic");
        EnsureAssets();
        for (int i = transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(transform.GetChild(i).gameObject);

        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        Transform ground = Group("Land and Arrival", transform);
        Transform roads = Group("Connected Road Network", transform);
        Transform buildings = Group("Commercial Blocks", transform);
        Transform landscape = Group("Street Trees and Landscape", transform);
        Transform details = Group("Street Furniture and Signs", transform);
        Transform traffic = Group("Traffic, Pedestrians and Cyclists", transform);
        Transform spawnGroup = Group("Exterior Spawn", transform);

        BuildGroundAndHills(ground);
        // Buildings come first so the streets, trees and lamps can keep clear of their footprints.
        BuildBuildings(buildings);
        BuildRoads(roads);
        BuildArrivalPlaza(ground);
        BuildDealershipSign(ground);
        BuildLandscaping(landscape);
        BuildStreetDetails(details);
        BuildTraffic(traffic);
        BuildSpawn(spawnGroup);
        ConfigureDaylight();
    }

    [MenuItem("Aurelle/Build Exterior District")]
    private static void BuildActiveScene()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Stop Play Mode before building the exterior district.");
            return;
        }
        // Inactive objects count too, so a disabled district is rebuilt in place instead of duplicated.
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        GameObject root = Array.Find(roots, candidate => candidate.name == "Exterior Environment");
        for (int i = 0; root == null && i < roots.Length; i++)
        {
            ExteriorEnvironmentBuilder existing = roots[i].GetComponentInChildren<ExteriorEnvironmentBuilder>(true);
            if (existing != null) root = existing.gameObject;
        }
        if (root == null)
        {
            root = new GameObject("Exterior Environment");
            Undo.RegisterCreatedObjectUndo(root, "Create Exterior Environment");
        }
        ExteriorEnvironmentBuilder builder = root.GetComponent<ExteriorEnvironmentBuilder>();
        if (builder == null) builder = Undo.AddComponent<ExteriorEnvironmentBuilder>(root);
        builder.BuildEnvironment();
    }

    private void EnsureAssets()
    {
        materials.Clear();
        asphalt = Mat("Asphalt", new Color(.58f,.60f,.62f), .02f,.28f, "AsphaltGrain", new Vector2(72,4));
        asphaltVertical = Mat("Asphalt Vertical", new Color(.58f,.60f,.62f), .02f,.28f, "AsphaltGrain", new Vector2(4,62.5f));
        concrete = Mat("Sidewalk Concrete", new Color(.72f,.72f,.69f), .02f,.36f, "ConcreteFine", new Vector2(140,2));
        ApplyPbrMaps(asphalt, "asphalt_02_diff_2k.jpg", "asphalt_02_nor_dx_2k.png", "asphalt_02_metallic_smoothness.png", .45f, .62f);
        ApplyPbrMaps(asphaltVertical, "asphalt_02_diff_2k.jpg", "asphalt_02_nor_dx_2k.png", "asphalt_02_metallic_smoothness.png", .45f, .62f);
        ApplyPbrMaps(concrete, "granular_concrete_diff_2k.jpg", "granular_concrete_nor_dx_2k.jpg", "granular_concrete_metallic_smoothness.png", .32f, .52f);
        paving = Mat("Arrival Limestone", new Color(.68f,.65f,.57f), .08f,.38f, "PaverJoint", new Vector2(12,12));
        facadeCream = Mat("Facade Light Limestone", new Color(.72f,.70f,.64f), .08f,.36f, "FacadeStone", new Vector2(4,4));
        facadeWarm = Mat("Facade Warm Brick", new Color(.47f,.34f,.27f), .03f,.28f, "FacadeStone", new Vector2(5,4));
        facadeGraphite = Mat("Facade Graphite", new Color(.17f,.21f,.24f), .15f,.45f, "FacadeStone", new Vector2(4,4));
        facadeBlue = Mat("Facade Blue Ceramic", new Color(.25f,.37f,.45f), .10f,.52f, "FacadeStone", new Vector2(4,4));
        glass = Mat("Urban Reflective Glass", new Color(.15f,.23f,.29f), .35f,.82f);
        metal = Mat("Street Brushed Metal", new Color(.35f,.39f,.41f), .72f,.65f);
        gold = Mat("Dealership Champagne Metal", new Color(.72f,.52f,.27f), .70f,.75f);
        foliage = Mat("Urban Evergreen", new Color(.20f,.31f,.19f), .0f,.3f, "LeafVariation", new Vector2(1,1));
        foliageLight = Mat("Urban Leaf Sunlit", new Color(.34f,.43f,.24f), .0f,.3f, "LeafVariation", new Vector2(1,1));
        trunk = Mat("Tree Bark", new Color(.29f,.20f,.14f), .0f,.28f);
        roadLine = Mat("Road Marking Ivory", new Color(.91f,.89f,.79f), .0f,.36f);
        bikeLane = Mat("Bicycle Lane Green", new Color(.20f,.34f,.29f), .0f,.3f);
        signFace = Mat("Urban Sign Enamel", new Color(.055f,.13f,.18f), .18f,.72f);
        redOn = Emissive("Signal Red Lit", new Color(.82f,.055f,.035f), 2.2f);
        redOff = Emissive("Signal Red Unlit", new Color(.17f,.025f,.02f), .02f);
        amberOn = Emissive("Signal Amber Lit", new Color(1f,.49f,.04f), 2.0f);
        amberOff = Emissive("Signal Amber Unlit", new Color(.2f,.09f,.015f), .02f);
        greenOn = Emissive("Signal Green Lit", new Color(.04f,.68f,.19f), 2.0f);
        greenOff = Emissive("Signal Green Unlit", new Color(.02f,.13f,.05f), .02f);
        walkOn = Emissive("Walk Signal Lit", new Color(.08f,.9f,.36f), 1.8f);
        walkOff = Emissive("Walk Signal Unlit", new Color(.025f,.14f,.06f), .02f);
        tire = Mat("Traffic Tire Rubber", new Color(.035f,.042f,.047f), .0f,.20f);
        wheelMetal = Mat("Traffic Wheel Satin Alloy", new Color(.57f,.59f,.58f), .8f,.72f);
        headlight = Emissive("Traffic Headlamp", new Color(.95f,.89f,.66f), .5f);
        tailLight = Emissive("Traffic Tail Lamp", new Color(.72f,.04f,.025f), .35f);
        carBlue = Mat("Traffic Car Deep Blue", new Color(.08f,.20f,.34f), .63f,.78f);
        carSilver = Mat("Traffic Car Silver", new Color(.53f,.59f,.63f), .74f,.78f);
        carRed = Mat("Traffic Car Garnet", new Color(.43f,.075f,.055f), .58f,.76f);
        carWhite = Mat("Traffic Car Ivory", new Color(.81f,.80f,.73f), .37f,.7f);
        groundMesh = BuildGroundMeshAsset();
        hillMesh = BuildHillMeshAsset();
        sedanBodyMesh = BuildSedanMeshAsset("TrafficSedanBody", false);
        sedanCabinMesh = BuildSedanMeshAsset("TrafficSedanCabin", true);
        // Traffic agents drive along +Z; the Tesla model's nose points along -Z.
        trafficVehiclePrefabs = new[]
        {
            BuildTrafficVehiclePrefab("Assets/Models/ShowroomCollection/BMW_M3_E30.glb", "TrafficVehicle_E30.prefab", 0f),
            BuildTrafficVehiclePrefab("Assets/Models/ShowroomCollection/BMW_M4_Competition.glb", "TrafficVehicle_M4.prefab", 0f),
            BuildTrafficVehiclePrefab("Assets/Models/ShowroomCollection/Tesla_Model_3_2018.glb", "TrafficVehicle_Tesla.prefab", 180f)
        };
    }

    private void BuildGroundAndHills(Transform parent)
    {
        GameObject ground = new GameObject("Rolling City Ground");
        ground.transform.SetParent(parent, false);
        ground.transform.position = Vector3.zero;
        ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
        ground.AddComponent<MeshRenderer>().sharedMaterial = Mat("Urban Grassland", new Color(.34f,.39f,.29f), .0f,.3f,"GrassGrain",new Vector2(38,34));
        ground.AddComponent<MeshCollider>().sharedMesh = groundMesh;
        Static(ground);
        MeshObject("Distant Rolling Hills", parent, hillMesh, Mat("Distant Hill Vegetation", new Color(.30f,.38f,.30f),.0f,.38f,"GrassGrain",new Vector2(5,3)), Vector3.zero, true);

        // A broad forecourt apron meets the existing showroom threshold at z=18 without covering the entrance.
        Box(parent, "Arrival Plaza Paved Apron", new Vector3(1f,-.025f,35.8f), new Vector3(48f,.10f,35.6f), paving, true);
        Box(parent, "Warm Stone Entry Walk", new Vector3(1f,.015f,30.5f), new Vector3(9.5f,.035f,25f), facadeCream, true);
        for (int i=0;i<12;i++)
            Box(parent,"Entry Paver Joint",new Vector3(-22f+i*4.2f,.031f,35.8f),new Vector3(.035f,.006f,35.0f),concrete,false);
    }

    private void BuildRoads(Transform parent)
    {
        foreach(float z in EastWestStreets) BuildEastWestStreet(parent,z);
        foreach(float x in NorthSouthStreets) BuildNorthSouthStreet(parent,x);
        // Raised driveways keep the showroom approach and west parking connected to the arterial.
        Box(parent,"Dealership Connector Road",new Vector3(1f,-.018f,53.8f),new Vector3(10.5f,.075f,13.0f),asphalt,true);
        Box(parent,"Parking Connector Road",new Vector3(-50f,-.018f,53.8f),new Vector3(8f,.075f,13.0f),asphalt,true);
        PaintCrossing(parent,1f,60f,true);
        PaintCrossing(parent,96f,60f,false);
        PaintCrossing(parent,96f,118f,false);
        PaintCrossing(parent,-96f,60f,false);
        PaintCrossing(parent,-96f,118f,false);
        BuildParking(parent);
    }

    private void BuildEastWestStreet(Transform parent,float z)
    {
        Box(parent,"East West Asphalt",new Vector3(0f,-.018f,z),new Vector3(280f,.09f,13f),asphalt,true);
        for(int side=-1;side<=1;side+=2)
        {
            // Curbs and sidewalks stop at the cross streets, so junctions and their crossings stay level.
            var curbGaps=new List<Vector2>();
            var walkGaps=new List<Vector2>();
            foreach(float x in NorthSouthStreets){curbGaps.Add(new Vector2(x-6.8f,x+6.8f));walkGaps.Add(new Vector2(x-13.8f,x+13.8f));}
            if(Mathf.Approximately(z,60f))
            {
                // Dropped curbs for the dealership and parking driveways, the cycle track connectors and the crossing's north end.
                if(side<0){curbGaps.Add(new Vector2(-4.25f,6.25f));curbGaps.Add(new Vector2(-54f,-46f));}
                else{curbGaps.Add(new Vector2(-128.9f,-127.1f));curbGaps.Add(new Vector2(127.1f,128.9f));curbGaps.Add(new Vector2(-5.5f,7.5f));}
            }
            float curbZ=z+side*6.65f,sidewalkZ=z+side*11.8f;
            FootprintGaps(curbGaps,curbZ-.15f,curbZ+.15f,true);
            FootprintGaps(walkGaps,sidewalkZ-2f,sidewalkZ+2f,true);
            foreach(Vector2 run in Runs(-140f,140f,curbGaps))
                Box(parent,"Raised Granite Curb",new Vector3((run.x+run.y)*.5f,.035f,curbZ),new Vector3(run.y-run.x,.22f,.30f),facadeCream,false);
            List<Vector2> walks=Runs(-140f,140f,walkGaps);
            foreach(Vector2 run in walks)
                Box(parent,"Concrete Sidewalk",new Vector3((run.x+run.y)*.5f,.015f,sidewalkZ),new Vector3(run.y-run.x,.08f,4.0f),SidewalkMaterial(run.y-run.x,true),true);
            for(float x=-132;x<=132;x+=12f)
                if(InRuns(x,walks)) Box(parent,"Sidewalk Control Joint",new Vector3(x,.057f,sidewalkZ),new Vector3(.035f,.006f,3.8f),paving,false);
        }
        // The dealership crossing at x=1 and its stop lines at x=1±9 stay clear of lane markings.
        bool dealershipCrossing=Mathf.Approximately(z,60f);
        for(float x=-136;x<=136;x+=7.5f)
        {
            if(NearAny(x,NorthSouthStreets,8.6f) || (dealershipCrossing && Mathf.Abs(x-1f)<11.3f))continue;
            Box(parent,"Broken Center Line",new Vector3(x,.032f,z),new Vector3(4.2f,.018f,.13f),roadLine,false);
            Box(parent,"Lane Dash",new Vector3(x,.032f,z-3.7f),new Vector3(2.7f,.016f,.10f),roadLine,false);
            Box(parent,"Lane Dash",new Vector3(x,.032f,z+3.7f),new Vector3(2.7f,.016f,.10f),roadLine,false);
        }
        for(int x=-120;x<=120;x+=30)
        {
            if(NearAny(x,NorthSouthStreets,7.5f) || (dealershipCrossing && Mathf.Abs(x-1f)<11.3f))continue;
            CreateArrow(parent,new Vector3(x,.033f,z-2f),Quaternion.Euler(0f,90f,0f));
            CreateArrow(parent,new Vector3(x,.033f,z+2f),Quaternion.Euler(0f,270f,0f));
        }
    }

    private void BuildNorthSouthStreet(Transform parent,float x)
    {
        Box(parent,"North South Asphalt",new Vector3(x,-.018f,(NorthSouthStart+NorthSouthEnd)*.5f),new Vector3(13f,.09f,NorthSouthEnd-NorthSouthStart),asphaltVertical,true);
        for(int side=-1;side<=1;side+=2)
        {
            float curbX=x+side*6.65f,sidewalkX=x+side*11.8f;
            // The north cycle track crosses at z=75.
            var curbGaps=new List<Vector2>{new Vector2(74.1f,75.9f)};
            var walkGaps=new List<Vector2>();
            foreach(float z in EastWestStreets){curbGaps.Add(new Vector2(z-6.5f,z+6.5f));walkGaps.Add(new Vector2(z-6.8f,z+6.8f));}
            FootprintGaps(curbGaps,curbX-.15f,curbX+.15f,false);
            FootprintGaps(walkGaps,sidewalkX-2f,sidewalkX+2f,false);
            List<Vector2> walks=Runs(NorthSouthStart,NorthSouthEnd,walkGaps);
            foreach(Vector2 run in Runs(NorthSouthStart,NorthSouthEnd,curbGaps))
                Box(parent,"Raised Granite Curb",new Vector3(curbX,.035f,(run.x+run.y)*.5f),new Vector3(.30f,.22f,run.y-run.x),facadeCream,false);
            foreach(Vector2 run in walks)
                Box(parent,"Concrete Sidewalk",new Vector3(sidewalkX,.015f,(run.x+run.y)*.5f),new Vector3(4f,.08f,run.y-run.x),SidewalkMaterial(run.y-run.x,false),true);
            for(float z=-65;z<=140;z+=12f)
                if(InRuns(z,walks)) Box(parent,"Sidewalk Control Joint",new Vector3(sidewalkX,.057f,z),new Vector3(3.8f,.006f,.035f),paving,false);
        }
        for(float z=-70;z<=140;z+=7.5f)
        {
            if(NearAny(z,EastWestStreets,8.6f) || z-2.1f<NorthSouthStart || z+2.1f>NorthSouthEnd)continue;
            Box(parent,"Broken Center Line",new Vector3(x,.032f,z),new Vector3(.13f,.018f,4.2f),roadLine,false);
            Box(parent,"Lane Dash",new Vector3(x-3.7f,.032f,z),new Vector3(.10f,.016f,2.7f),roadLine,false);
            Box(parent,"Lane Dash",new Vector3(x+3.7f,.032f,z),new Vector3(.10f,.016f,2.7f),roadLine,false);
        }
        for(int z=-60;z<=130;z+=30)
        {
            if(NearAny(z,EastWestStreets,7.5f) || z-1f<NorthSouthStart || z+1f>NorthSouthEnd)continue;
            // Right-hand traffic: southbound on the west half, northbound on the east half.
            CreateArrow(parent,new Vector3(x-2f,.033f,z),Quaternion.Euler(0f,180f,0f));
            CreateArrow(parent,new Vector3(x+2f,.033f,z),Quaternion.identity);
        }
    }

    // The pieces of [from,to] left between the gaps; x and y are the start and end of each gap and run.
    private static List<Vector2> Runs(float from,float to,List<Vector2> gaps)
    {
        gaps.Sort((a,b)=>a.x.CompareTo(b.x));
        var runs=new List<Vector2>();
        float start=from;
        foreach(Vector2 gap in gaps)
        {
            if(gap.x-start>.01f)runs.Add(new Vector2(start,Mathf.Min(gap.x,to)));
            start=Mathf.Max(start,gap.y);
            if(start>=to)break;
        }
        if(to-start>.01f)runs.Add(new Vector2(start,to));
        return runs;
    }

    // Adds a gap for every building whose footprint crosses the band [bandMin,bandMax] of a run along x (or along z).
    private void FootprintGaps(List<Vector2> gaps,float bandMin,float bandMax,bool alongX)
    {
        foreach(Rect footprint in buildingFootprints)
        {
            if(alongX && footprint.yMin<bandMax && footprint.yMax>bandMin) gaps.Add(new Vector2(footprint.xMin,footprint.xMax));
            else if(!alongX && footprint.xMin<bandMax && footprint.xMax>bandMin) gaps.Add(new Vector2(footprint.yMin,footprint.yMax));
        }
    }

    private bool NearBuilding(Vector3 p,float clearance)
    {
        foreach(Rect footprint in buildingFootprints)
            if(p.x>=footprint.xMin-clearance && p.x<=footprint.xMax+clearance && p.z>=footprint.yMin-clearance && p.z<=footprint.yMax+clearance)return true;
        return false;
    }

    private static bool InRuns(float value,List<Vector2> runs)
    {
        foreach(Vector2 run in runs) if(value>run.x+.1f && value<run.y-.1f) return true;
        return false;
    }

    private static bool NearAny(float value,float[] centres,float distance)
    {
        foreach(float centre in centres) if(Mathf.Abs(value-centre)<distance) return true;
        return false;
    }

    private void PaintCrossing(Transform parent,float x,float z,bool acrossEastWest)
    {
        int count=14;
        for(int i=0;i<count;i++)
        {
            if(acrossEastWest)
                Box(parent,"Raised Zebra Crossing Stripe",new Vector3(x-6.2f+i*.95f,.038f,z),new Vector3(.47f,.018f,11.2f),roadLine,false);
            else
                Box(parent,"Raised Zebra Crossing Stripe",new Vector3(x,.038f,z-6.2f+i*.95f),new Vector3(11.2f,.018f,.47f),roadLine,false);
        }
        if(acrossEastWest)
        {
            // Each stop line spans its approach lane only: eastbound from the avenue cycle lane (to z-4.7) up to the centre line,
            // westbound from the centre line to the north curb.
            Box(parent,"Approach Stop Line",new Vector3(x-9f,.039f,z-2.35f),new Vector3(.34f,.018f,4.7f),roadLine,false);
            Box(parent,"Approach Stop Line",new Vector3(x+9f,.039f,z+3.25f),new Vector3(.34f,.018f,6.5f),roadLine,false);
        }
    }

    private void BuildParking(Transform parent)
    {
        Box(parent,"Dealership Customer Parking Asphalt",new Vector3(-51f,-.012f,15f),new Vector3(27f,.08f,45f),asphalt,true);
        Box(parent,"Parking Aisle",new Vector3(-51f,.035f,13f),new Vector3(25f,.018f,10f),roadLine,false);
        for(int i=0;i<8;i++)
        {
            float x=-62.0f+i*3.15f;
            Box(parent,"Customer Parking Stall Line",new Vector3(x,.035f,25f),new Vector3(.11f,.018f,18f),roadLine,false);
        }
        Box(parent,"Accessible Stall Blue Field",new Vector3(-61.4f,.033f,17f),new Vector3(5.6f,.014f,13f),bikeLane,false);
        Box(parent,"Accessible Stall Blue Field",new Vector3(-55.2f,.033f,17f),new Vector3(5.6f,.014f,13f),bikeLane,false);
        for(int i=0;i<4;i++)
            BuildTrafficCar(parent,new Vector3(-60f+i*5.8f,.0f,17f+(i%2)*5f),Quaternion.Euler(0f,180f,0f),i%4,true);
        Sign(parent,"Accessible Parking","P  |  ACCESSIBLE",new Vector3(-60.5f,0f,25.2f),Quaternion.identity,2.5f);
    }

    private void BuildArrivalPlaza(Transform parent)
    {
        // Low planters define a wide, unobstructed accessible route from the entrance to the crossing.
        for(int side=-1;side<=1;side+=2)
        {
            float x=1f+side*20f;
            Box(parent,"Stone Landscape Planter",new Vector3(x,.28f,34f),new Vector3(6.4f,.55f,16f),facadeGraphite,true);
            Box(parent,"Planter Soil",new Vector3(x,.57f,34f),new Vector3(5.9f,.04f,15.5f),trunk,false);
            for(int i=0;i<4;i++)
            {
                float z=27f+i*4.5f;
                AddShrub(parent,new Vector3(x+(i%2==0?-.8f:.8f),.62f,z),1.35f,foliageLight);
            }
        }
        // Tactile paving and a gentle ramp at the public sidewalk edge.
        Box(parent,"Tactile Paving Warning Band",new Vector3(1f,.042f,50.4f),new Vector3(9.2f,.025f,.75f),gold,false);
        Box(parent,"Accessible Ramp",new Vector3(1f,.005f,52.0f),new Vector3(9.5f,.04f,3.0f),concrete,true);
        // The crossing's north end: a paved landing from the dropped curb across the verge to the north sidewalk.
        Box(parent,"Accessible Ramp",new Vector3(1f,.005f,68.15f),new Vector3(13f,.04f,3.3f),SidewalkMaterial(13f,true),true);
        Box(parent,"Cycle Track Along Avenue",new Vector3(1f,.025f,54.4f),new Vector3(280f,.045f,1.8f),bikeLane,true);
        for(int x=-130;x<=130;x+=18)
            CreateArrow(parent,new Vector3(x,.052f,54.4f),Quaternion.Euler(0f,90f,0f),.64f);
        // A second cycle path runs along the north sidewalk as a clear, connected track.
        Box(parent,"North Side Cycle Track",new Vector3(0f,.023f,75.0f),new Vector3(280f,.04f,1.8f),bikeLane,true);
        Box(parent,"West Cycle Track Connector",new Vector3(-128f,.023f,64.7f),new Vector3(1.8f,.04f,20.6f),bikeLane,true);
        Box(parent,"East Cycle Track Connector",new Vector3(128f,.023f,64.7f),new Vector3(1.8f,.04f,20.6f),bikeLane,true);
        for(int x=-130;x<=130;x+=18)
            CreateArrow(parent,new Vector3(x,.05f,75f),Quaternion.Euler(0f,-90f,0f),.64f);
    }

    private void BuildDealershipSign(Transform parent)
    {
        Box(parent,"Aurelle Exterior Sign Backplate",new Vector3(1f,4.15f,20.22f),new Vector3(10.8f,1.20f,.22f),signFace,false);
        Box(parent,"Aurelle Sign Gold Top Reveal",new Vector3(1f,4.77f,20.10f),new Vector3(10.5f,.045f,.06f),gold,false);
        CreateText(parent,"AURELLE  |  MOTORS",new Vector3(1f,4.13f,20.38f),Quaternion.identity,.31f,new Color(.92f,.78f,.50f),TextAlignment.Center,new Vector2(9.6f,.88f));
        // Small wayfinding pylons are outside the clear entrance path.
        for(int side=-1;side<=1;side+=2)
        {
            Box(parent,"Arrival Wayfinding Pylon",new Vector3(1f+side*24f,1.25f,35f),new Vector3(.28f,2.5f,.28f),metal,true);
            // The display rests on the pylon's street face (z=35.14) and the depth-tested label sits 1 cm in front of it.
            Box(parent,"Pylon Display",new Vector3(1f+side*24f,2.22f,35.165f),new Vector3(.20f,.50f,.05f),signFace,false);
            CreateText(parent,side<0?"SALES":"SERVICE",new Vector3(1f+side*24f,2.22f,35.20f),Quaternion.identity,.10f,new Color(.94f,.81f,.57f),TextAlignment.Center,new Vector2(.17f,.16f));
        }
    }

    private void BuildBuildings(Transform parent)
    {
        buildingFootprints.Clear();
        Building(parent,"Aurelle Quarter Market",new Vector3(-46f,0f,92f),180f,29f,18f,20f,5,facadeCream,"GROCERY  |  MARKET 24");
        Building(parent,"NOVA Pharmacy",new Vector3(46f,0f,92f),180f,24f,18f,12f,3,facadeBlue,"NOVA PHARMACY");
        Building(parent,"North Offices",new Vector3(10f,0f,143f),180f,34f,18f,28f,7,facadeGraphite,"AURELLE BUSINESS CENTRE");
        Building(parent,"North Residence West",new Vector3(-55f,0f,143f),180f,22f,18f,24f,6,facadeWarm,"RESIDENCES");
        Building(parent,"North Residence East",new Vector3(56f,0f,143f),180f,22f,18f,20f,5,facadeCream,"CITY LIVING");
        Building(parent,"Far North Office",new Vector3(112f,0f,143f),180f,26f,18f,30f,8,facadeBlue,"FINANCE  |  CONSULTING");
        Building(parent,"South Cafe",new Vector3(-44f,0f,-72f),0f,25f,18f,10f,2,facadeWarm,"KAFE  |  ROASTERY");
        Building(parent,"South Bakery",new Vector3(43f,0f,-72f),0f,23f,18f,9f,2,facadeCream,"BAKERY  07:00");
        Building(parent,"South Apartments",new Vector3(100f,0f,-72f),0f,27f,18f,24f,6,facadeGraphite,"APARTMENTS");
        Building(parent,"West Apartments",new Vector3(-125f,0f,-18f),90f,24f,21f,27f,7,facadeBlue,"PARK RESIDENCE");
        Building(parent,"West Corner Retail",new Vector3(-125f,0f,35f),90f,20f,19f,11f,3,facadeCream,"BOOKS  |  GIFTS");
        Building(parent,"West Hotel",new Vector3(-125f,0f,91f),90f,30f,24f,29f,8,facadeWarm,"HOTEL AURELLE");
        Building(parent,"East Market Street Shops",new Vector3(125f,0f,-19f),-90f,22f,20f,12f,3,facadeCream,"CITY BICYCLE");
        Building(parent,"East Apartments",new Vector3(125f,0f,37f),-90f,23f,20f,23f,6,facadeGraphite,"PARKSIDE LIVING");
        Building(parent,"East Corner Offices",new Vector3(125f,0f,91f),-90f,28f,23f,27f,7,facadeBlue,"DESIGN STUDIO");
        Building(parent,"South West Storefront",new Vector3(-76f,0f,18f),0f,22f,18f,9f,2,facadeBlue,"BAKERY  |  FLOWERS");
        Building(parent,"South East Showroom Block",new Vector3(73f,0f,16f),0f,25f,19f,12f,3,facadeWarm,"FURNITURE  |  HOME");
        Building(parent,"Courtyard Offices",new Vector3(39f,0f,-19f),0f,21f,17f,15f,4,facadeCream,"CLINIC  |  WELLNESS");
    }

    private void Building(Transform parent,string name,Vector3 position,float yaw,float width,float depth,float height,int floors,Material wall,string sign)
    {
        Transform root=NewEmpty(name,parent);
        root.localPosition=position;
        root.localRotation=Quaternion.Euler(0f,yaw,0f);
        float c=Mathf.Abs(Mathf.Cos(yaw*Mathf.Deg2Rad)),s=Mathf.Abs(Mathf.Sin(yaw*Mathf.Deg2Rad));
        Vector2 half=new Vector2(c*width+s*depth,s*width+c*depth)*.5f;
        buildingFootprints.Add(Rect.MinMaxRect(root.position.x-half.x,root.position.z-half.y,root.position.x+half.x,root.position.z+half.y));
        Box(root,"Facade Mass",new Vector3(0f,height*.5f,0f),new Vector3(width,height,depth),wall,true);
        Box(root,"Ground Floor Plinth",new Vector3(0f,.24f,0f),new Vector3(width+.25f,.48f,depth+.2f),facadeGraphite,false);
        Box(root,"Roof Parapet",new Vector3(0f,height+.40f,0f),new Vector3(width+.5f,.8f,depth+.45f),facadeGraphite,false);
        // Above the header band (top 3.62), so the depth-tested sign below it stays whole from the street.
        Box(root,"Street Side Canopy",new Vector3(0f,3.72f,depth*.5f+.48f),new Vector3(width-.4f,.18f,1.05f),metal,false);
        int bays=Mathf.Max(3,Mathf.FloorToInt(width/3.4f));
        float bay=width/bays;
        // Street-level glazing and mullions.
        for(int i=0;i<bays;i++)
        {
            float x=-width*.5f+bay*(i+.5f);
            float glassWidth=Mathf.Max(1.25f,bay-.22f);
            Box(root,"Storefront Reflective Glazing",new Vector3(x,1.82f,depth*.5f+.045f),new Vector3(glassWidth,2.55f,.08f),glass,false);
            Box(root,"Storefront Mullion",new Vector3(x-bay*.5f+.04f,1.83f,depth*.5f+.11f),new Vector3(.11f,2.72f,.12f),metal,false);
        }
        Box(root,"Storefront Header Band",new Vector3(0f,3.23f,depth*.5f+.11f),new Vector3(width-.18f,.78f,.18f),signFace,false);
        CreateText(root,sign,new Vector3(0f,3.24f,depth*.5f+.22f),Quaternion.identity,.27f,new Color(.93f,.81f,.61f),TextAlignment.Center,new Vector2(width-1.2f,.52f));
        // Recessed framed entrances make the glazing read as actual street-level businesses.
        Box(root,"Shop Entrance Glass",new Vector3(0f,1.42f,depth*.5f+.12f),new Vector3(1.55f,2.72f,.07f),glass,false);
        Box(root,"Entrance Door Pull",new Vector3(.56f,1.45f,depth*.5f+.20f),new Vector3(.055f,.70f,.05f),gold,false);
        for(int floor=1;floor<floors;floor++)
        {
            float level=floor*(height/floors);
            Box(root,"Continuous Floor Reveal",new Vector3(0f,level,0f),new Vector3(width+.1f,.10f,depth+.1f),metal,false);
            for(int i=0;i<bays;i++)
            {
                float x=-width*.5f+bay*(i+.5f);
                float h=Mathf.Min(1.75f,height/floors*.54f);
                Box(root,"Upper Floor Window",new Vector3(x,level+(height/floors)*.53f,depth*.5f+.045f),new Vector3(bay*.69f,h,.08f),glass,false);
                Box(root,"Window Head",new Vector3(x,level+(height/floors)*.53f+h*.5f+.07f,depth*.5f+.105f),new Vector3(bay*.73f,.10f,.12f),metal,false);
                if((i+floor)%3==0 && floors>3)
                {
                    Box(root,"Slim Balcony Slab",new Vector3(x,level+(height/floors)*.25f,depth*.5f+.56f),new Vector3(bay*.82f,.12f,1.05f),facadeCream,false);
                    Box(root,"Slim Balcony Rail",new Vector3(x,level+(height/floors)*.25f+.52f,depth*.5f+1.02f),new Vector3(bay*.78f,.055f,.055f),metal,false);
                }
            }
        }
        // A few carefully placed rooftop plant rooms and screened mechanical units.
        Box(root,"Roof Mechanical Screen",new Vector3(-width*.23f,height+1.30f,0f),new Vector3(width*.28f,1.0f,depth*.52f),facadeGraphite,false);
        for(int i=0;i<2;i++)
            Box(root,"Rooftop Air Handling Unit",new Vector3(width*.19f+i*2.3f,height+1.0f,-depth*.12f),new Vector3(1.65f,.95f,1.35f),concrete,false);
    }

    private void BuildLandscaping(Transform parent)
    {
        plantedTrees.Clear();
        // Planted forecourt beds, kept outside the 9 m clear pedestrian corridor. Planted first so street trees keep clear of them.
        for(int side=-1;side<=1;side+=2)
        {
            for(int i=0;i<5;i++)
            {
                Vector3 p=new Vector3(1f+side*28f,.02f,22f+i*5.0f);
                Tree(parent,p,.72f+(i%3)*.09f);
                for(int j=0;j<3;j++) AddShrub(parent,p+new Vector3(side*2f,.20f,(j-1)*1.1f),.8f,foliage);
            }
        }
        foreach(float z in EastWestStreets)
        {
            for(int x=-126;x<=126;x+=26)
            {
                if(Mathf.Abs(x-1f)<13f && z==60f) continue;
                TreeIfClear(parent,new Vector3(x,.01f,z-17f),.86f+(Mathf.Abs(x)%3)*.08f);
                TreeIfClear(parent,new Vector3(x+9f,.01f,z+17f),.78f+(Mathf.Abs(x)%4)*.06f);
            }
        }
        // North-south street trees stand in the planting verge between curb and sidewalk.
        foreach(float x in NorthSouthStreets)
        {
            for(int z=-35;z<=130;z+=28)
            {
                TreeIfClear(parent,new Vector3(x-8.3f,.01f,z),.83f);
                TreeIfClear(parent,new Vector3(x+8.3f,.01f,z+8f),.94f);
            }
        }
        // Flower beds at crossings and sidewalks.
        for(int side=-1;side<=1;side+=2)
        {
            Box(parent,"Linear Ornamental Planting Bed",new Vector3(side*72f,.16f,82f),new Vector3(10f,.28f,2.2f),facadeGraphite,false);
            for(int i=0;i<6;i++) AddShrub(parent,new Vector3(side*72f-4f+i*1.6f,.32f,82f),.65f,(i%2==0)?foliageLight:foliage);
        }
    }

    private void BuildStreetDetails(Transform parent)
    {
        for(int i=0;i<12;i++)
        {
            float x=-132f+i*24f;
            LampIfClear(parent,new Vector3(x,.0f,45.6f),Vector3.right);
            // In the strip between the north sidewalk (to z=73.8) and the north cycle track (from z=74.1).
            LampIfClear(parent,new Vector3(x,.0f,73.95f),Vector3.right);
        }
        for(int i=0;i<6;i++)
        {
            float z=-33f+i*30f;
            // z=57 and z=117 fall on the avenues' carriageways.
            if(NearAny(z,EastWestStreets,7.3f))continue;
            LampIfClear(parent,new Vector3(-82f,.0f,z),Vector3.forward);
            LampIfClear(parent,new Vector3(82f,.0f,z),Vector3.forward);
        }
        Sign(parent,"Speed Limit 30","30",new Vector3(18f,0f,69f),Quaternion.Euler(0f,180f,0f),2.4f);
        Sign(parent,"Pedestrian Crossing","PEDESTRIAN\nCROSSING",new Vector3(13f,0f,52.0f),Quaternion.identity,2.1f);
        Sign(parent,"District Direction","DEALERSHIP  ←\nMARKET  →",new Vector3(-7f,0f,52.0f),Quaternion.identity,2.3f);
        // On the east verge of the x=96 street, between its curb (x 102.5-102.8) and sidewalk (from x=105.8).
        Sign(parent,"Northbound Direction","CENTRE  ↑",new Vector3(104.3f,0f,83f),Quaternion.Euler(0f,-90f,0f),2.2f);
        for(int side=-1;side<=1;side+=2)
        {
            float x=1f+side*10.6f;
            Bench(parent,new Vector3(x,.05f,40f));
            StreetBin(parent,new Vector3(x,.0f,32.5f));
            CreateBikeRack(parent,new Vector3(x,.0f,26.5f));
        }
        for(int i=0;i<6;i++)
        {
            float x=-62f+i*4.2f;
            Box(parent,"Parking Bollard",new Vector3(x,.52f,28.4f),new Vector3(.20f,1.04f,.20f),metal,false);
        }
        // Utility cabinets and subtle drainage grates ground the street edges.
        foreach(float x in new[]{-108f,-38f,38f,108f})
        {
            Box(parent,"Street Utility Cabinet",new Vector3(x,.73f,47.8f),new Vector3(1.0f,1.45f,.68f),facadeGraphite,true);
            for(int i=0;i<5;i++) Box(parent,"Cabinet Vent",new Vector3(x,.45f+i*.13f,47.43f),new Vector3(.48f,.025f,.018f),metal,false);
            Box(parent,"Stormwater Drain Grate",new Vector3(x,.042f,53.0f),new Vector3(1.2f,.02f,.42f),metal,false);
        }
    }

    private void BuildTraffic(Transform parent)
    {
        Transform signalGroup=Group("Signalized Intersections",parent);
        var signals=new List<ExteriorTrafficSignal>();
        // Walk signals stand on sidewalks: south of the dealership crossing, and on the east north-south sidewalk at each junction.
        Vector3 junctionWalkSignal=new Vector3(10.2f,0f,-7.4f);
        signals.Add(BuildSignal(signalGroup,"Central Pedestrian Crossing Signal",1f,60f,0f,new Vector3(8.2f,0f,-10.3f)));
        signals.Add(BuildSignal(signalGroup,"West South Junction Signal",-96f,60f,5f,junctionWalkSignal));
        signals.Add(BuildSignal(signalGroup,"East South Junction Signal",96f,60f,10f,junctionWalkSignal));
        signals.Add(BuildSignal(signalGroup,"West North Junction Signal",-96f,118f,15f,junctionWalkSignal));
        signals.Add(BuildSignal(signalGroup,"East North Junction Signal",96f,118f,20f,junctionWalkSignal));

        Transform routes=Group("Traffic Routes",parent);
        Transform[] route=BuildLoopRoute(routes);
        Transform vehicles=Group("Moving Road Traffic",parent);
        for(int i=0;i<4;i++)
        {
            int index=(i*5)%route.Length;
            GameObject car=CreateTrafficCarVisual("Traffic Vehicle "+(i+1),vehicles,i);
            if(car==null)continue;
            car.transform.position=route[index].position;
            if(index+1<route.Length) car.transform.rotation=Quaternion.LookRotation(route[index+1].position-route[index].position,Vector3.up);
            ExteriorTrafficCarAgent agent=car.AddComponent<ExteriorTrafficCarAgent>();
            agent.Configure(route,signals.ToArray(),7.2f+(i%3)*.65f,0f);
            agent.SetStartIndex(index+1);
        }
        Transform[] reverseRoute=BuildReverseLoopRoute(routes);
        for(int i=0;i<2;i++)
        {
            int index=(i*reverseRoute.Length/2)%reverseRoute.Length;
            GameObject car=CreateTrafficCarVisual("Opposing Traffic Vehicle "+(i+1),vehicles,i+1);
            if(car==null)continue;
            car.transform.position=reverseRoute[index].position;
            car.transform.rotation=Quaternion.LookRotation(reverseRoute[(index+1)%reverseRoute.Length].position-reverseRoute[index].position,Vector3.up);
            ExteriorTrafficCarAgent agent=car.AddComponent<ExteriorTrafficCarAgent>();
            agent.Configure(reverseRoute,signals.ToArray(),7.0f+(i%2)*.7f,0f);
            agent.SetStartIndex(index+1);
        }

        // One articulated local route and one commuter path keep movement predictable and separated.
        Transform cyclists=Group("Protected Cycle Routes",parent);
        Transform[] cycleRoute=CreatePath(cyclists,"Avenue Cycle Track Route",new Vector3[]{
            new Vector3(-128f,0f,54.4f),new Vector3(128f,0f,54.4f),new Vector3(128f,0f,75f),new Vector3(-128f,0f,75f)
        },true);
        CreateCyclist(cyclists,"Commuter Cyclist",cycleRoute,0,signals.ToArray());
        CreateCyclist(cyclists,"Second Commuter Cyclist",cycleRoute,cycleRoute.Length/2,signals.ToArray());

        BuildPedestrians(parent,signals[0]);
        ApplyCrowdLooks(parent);
    }

    private ExteriorTrafficSignal BuildSignal(Transform parent,string name,float x,float z,float phaseOffset,Vector3 walkSignalOffset)
    {
        GameObject root=new GameObject(name);
        root.transform.SetParent(parent,false);
        root.transform.position=new Vector3(x,0f,z);
        ExteriorTrafficSignal signal=root.AddComponent<ExteriorTrafficSignal>();
        var ewReds=new List<Renderer>(); var ewAmbers=new List<Renderer>(); var ewGreens=new List<Renderer>();
        var nsReds=new List<Renderer>(); var nsAmbers=new List<Renderer>(); var nsGreens=new List<Renderer>();
        SignalHead(root.transform,new Vector3(x-7.15f,0f,z-7.3f),270f,ewReds,ewAmbers,ewGreens);
        SignalHead(root.transform,new Vector3(x+7.15f,0f,z+7.3f),90f,ewReds,ewAmbers,ewGreens);
        SignalHead(root.transform,new Vector3(x+7.3f,0f,z-7.15f),180f,nsReds,nsAmbers,nsGreens);
        SignalHead(root.transform,new Vector3(x-7.3f,0f,z+7.15f),0f,nsReds,nsAmbers,nsGreens);
        Transform walkSignal=NewEmpty("Pedestrian Signal",root.transform); walkSignal.position=root.transform.position+walkSignalOffset;
        Cylinder(walkSignal,"Pedestrian Signal Post",Vector3.up*1.25f,.07f,2.5f,metal);
        Box(walkSignal,"Pedestrian Signal Housing",new Vector3(0f,2.1f,0f),new Vector3(.42f,.76f,.30f),facadeGraphite,false);
        Renderer walk=Sphere(walkSignal,"Walk Indicator",new Vector3(0f,2.28f,.17f),.20f,walkOff,false);
        Renderer wait=Sphere(walkSignal,"Wait Indicator",new Vector3(0f,1.90f,.17f),.20f,redOff,false);
        signal.Configure(ewReds.ToArray(),ewAmbers.ToArray(),ewGreens.ToArray(),nsReds.ToArray(),nsAmbers.ToArray(),nsGreens.ToArray(),
            redOn,redOff,amberOn,amberOff,greenOn,greenOff,phaseOffset,
            walk,wait,walkOn,walkOff,redOn,redOff);
        return signal;
    }

    private void SignalHead(Transform parent,Vector3 position,float facingY,
        List<Renderer> reds,List<Renderer> ambers,List<Renderer> greens)
    {
        Transform head=NewEmpty("Signal Head",parent);head.position=position;head.rotation=Quaternion.Euler(0f,facingY,0f);
        Cylinder(head,"Signal Mast",new Vector3(0f,3.1f,0f),.105f,6.2f,metal);
        Box(head,"Signal Head Housing",new Vector3(0f,5.1f,0f),new Vector3(.47f,1.53f,.38f),facadeGraphite,false);
        reds.Add(Sphere(head,"Red Traffic Lamp",new Vector3(0f,5.60f,.205f),.28f,redOff,false));
        ambers.Add(Sphere(head,"Amber Traffic Lamp",new Vector3(0f,5.10f,.205f),.28f,amberOff,false));
        greens.Add(Sphere(head,"Green Traffic Lamp",new Vector3(0f,4.60f,.205f),.28f,greenOff,false));
    }

    // Both loops keep right of the centre lines, the perimeter loop on the outer side of each street and the counterflow loop on the inner side.
    private Transform[] BuildLoopRoute(Transform parent)
    {
        Vector3[] corners={new Vector3(-97.6f,0f,58.4f),new Vector3(97.6f,0f,58.4f),new Vector3(97.6f,0f,119.6f),new Vector3(-97.6f,0f,119.6f)};
        Transform[] points=CreatePath(parent,"Perimeter Traffic Loop",corners,true,8);
        return points;
    }

    private Transform[] BuildReverseLoopRoute(Transform parent)
    {
        Vector3[] corners={new Vector3(94.4f,0f,61.6f),new Vector3(-94.4f,0f,61.6f),new Vector3(-94.4f,0f,116f),new Vector3(94.4f,0f,116f)};
        return CreatePath(parent,"Counterflow Traffic Loop",corners,true,8);
    }

    private static void ConfigureRouteStart(ExteriorTrafficCarAgent agent,int startIndex)
    {
        // Initial route positions are spaced around the loop before the first update.
        agent.SetStartIndex(startIndex);
    }

    private Transform[] CreatePath(Transform parent,string name,Vector3[] anchors,bool closed,int subdivisions=1)
    {
        Transform route=NewEmpty(name,parent);
        List<Transform> points=new List<Transform>();
        int segments=closed?anchors.Length:anchors.Length-1;
        // Straight legs between the anchors; a curve through only the corners would swing out of the streets.
        for(int i=0;i<segments;i++)
        {
            Vector3 from=anchors[i];
            Vector3 to=anchors[(i+1)%anchors.Length];
            for(int j=0;j<subdivisions;j++)
            {
                Transform node=NewEmpty("Waypoint "+points.Count,route);
                node.position=Vector3.Lerp(from,to,j/(float)subdivisions);
                points.Add(node);
            }
        }
        if(!closed)
        {
            Transform node=NewEmpty("Waypoint "+points.Count,route);
            node.position=anchors[anchors.Length-1];
            points.Add(node);
        }
        return points.ToArray();
    }

    private void BuildPedestrians(Transform parent,ExteriorTrafficSignal crossingSignal)
    {
        Transform group=Group("Walking Pedestrians",parent);
        GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Presenter/Remy.fbx");
        if(prefab==null){Debug.LogWarning("Remy humanoid model is missing; pedestrian crowd was not generated.",this);return;}
        // The two forecourt loops walk around the bench, bin and bike racks at x=1±10.6.
        Vector3[][] paths={
            new[]{new Vector3(-12f,0f,26f),new Vector3(-7.5f,0f,26f),new Vector3(-7.5f,0f,42f),new Vector3(-12f,0f,42f)},
            new[]{new Vector3(14f,0f,26f),new Vector3(9.5f,0f,26f),new Vector3(9.5f,0f,42f),new Vector3(14f,0f,42f)},
            new[]{new Vector3(-81f,0f,46f),new Vector3(-25f,0f,46f),new Vector3(-25f,0f,47f),new Vector3(-81f,0f,47f)},
            new[]{new Vector3(24f,0f,46f),new Vector3(81f,0f,46f),new Vector3(81f,0f,47f),new Vector3(24f,0f,47f)},
            new[]{new Vector3(-114f,0f,70f),new Vector3(-114f,0f,73f),new Vector3(-55f,0f,73f),new Vector3(-55f,0f,70f)},
            new[]{new Vector3(53f,0f,70f),new Vector3(53f,0f,73f),new Vector3(115f,0f,73f),new Vector3(115f,0f,70f)},
            new[]{new Vector3(-121f,0f,-34f),new Vector3(-121f,0f,-31f),new Vector3(-70f,0f,-31f),new Vector3(-70f,0f,-34f)},
            new[]{new Vector3(71f,0f,-34f),new Vector3(71f,0f,-31f),new Vector3(121f,0f,-31f),new Vector3(121f,0f,-34f)}
        };
        for(int i=0;i<paths.Length;i++)
        {
            Transform[] route=CreatePath(group,"Sidewalk Route "+(i+1),paths[i],true,1);
            GameObject actor=(GameObject)PrefabUtility.InstantiatePrefab(prefab,group);
            actor.name="Pedestrian "+(i+1);
            actor.transform.position=route[0].position;
            actor.transform.localScale=Vector3.one*.42f;
            Animator animator=actor.GetComponent<Animator>();
            if(animator!=null) animator.runtimeAnimatorController=null;
            CharacterController cc=actor.GetComponent<CharacterController>();
            if(cc==null) cc=actor.AddComponent<CharacterController>();
            cc.height=4.1f; cc.radius=.48f; cc.center=new Vector3(0f,2.05f,0f); cc.skinWidth=.06f; cc.stepOffset=.25f;
            ExteriorPedestrianAgent agent=actor.AddComponent<ExteriorPedestrianAgent>();
            agent.Configure(route,.82f+(i%3)*.13f,.42f,i+1);
            if(i==2)
            {
                agent.Configure(route,1.05f,.42f,i+1,crossingSignal,Vector3.forward,new[]{1,3});
                actor.transform.position=new Vector3(1f,0f,47f);
                Transform[] crossing=CreatePath(group,"Signalized Showroom Crossing",new[]{new Vector3(1f,0f,47f),new Vector3(1f,0f,73f),new Vector3(1f,0f,47f)},false,1);
                agent.Configure(crossing,1.0f,.42f,i+1,crossingSignal,Vector3.forward,new[]{1,2});
            }
        }
    }

    // Every walker and rider is a Remy.fbx instance. Each one wears its own top, bottoms, shoes, hair colour and skin tone: textured
    // URP Lit materials that Aurelle > Realism > Upgrade People keeps in Assets/Materials/Realism/People/Crowd as
    // Crowd_<Top|Bottoms|Shoes|Hair|Skin>_<name>.mat. Eyes and eyelashes share Remy's own materials; a missing material keeps the model's.
    private const string PeopleMaterialFolder="Assets/Materials/Realism/People";
    // Top, bottoms, shoes, hair, skin; one look per pedestrian (Pedestrian 1..8) and per rider, in hierarchy order.
    private static readonly string[][] PedestrianLooks={
        new[]{"White","Denim","Classic","DarkBrown","Light"},
        new[]{"Navy","Khaki","Tan","Black","Medium"},
        new[]{"HeatherGrey","Black","Dark","Chestnut","Tan"},
        new[]{"Burgundy","DarkDenim","Classic","Black","Deep"},
        new[]{"Olive","Grey","Dark","Blond","Light"},
        new[]{"Charcoal","Khaki","Classic","DarkBrown","Tan"},
        new[]{"SkyBlue","DarkDenim","Dark","Ash","Light"},
        new[]{"Mustard","Black","Classic","Black","Medium"}
    };
    private static readonly string[][] CyclistLooks={
        new[]{"SteelBlue","Black","Dark","DarkBrown","Medium"},
        new[]{"Rust","Grey","Classic","Auburn","Light"}
    };

    /// <summary>Dresses every pedestrian and cyclist rider under <paramref name="root"/> in its crowd look (saved material
    /// assignments) and returns the number of renderers changed.</summary>
    public static int ApplyCrowdLooks(Transform root)
    {
        if(root==null)return 0;
        int changed=0;
        ExteriorPedestrianAgent[] walkers=root.GetComponentsInChildren<ExteriorPedestrianAgent>(true);
        for(int i=0;i<walkers.Length;i++) changed+=ApplyCrowdLook(walkers[i].gameObject,PedestrianLooks[i%PedestrianLooks.Length]);
        ExteriorCyclistAgent[] riders=root.GetComponentsInChildren<ExteriorCyclistAgent>(true);
        for(int i=0;i<riders.Length;i++)
        {
            Transform rider=riders[i].transform.Find("Cyclist Rider");
            if(rider!=null) changed+=ApplyCrowdLook(rider.gameObject,CyclistLooks[i%CyclistLooks.Length]);
        }
        return changed;
    }

    private static int ApplyCrowdLook(GameObject person,string[] look)
    {
        int changed=0;
        foreach(SkinnedMeshRenderer part in person.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string material;
            switch(part.name)
            {
                case "Tops": material="Crowd/Crowd_Top_"+look[0]; break;
                case "Bottoms": material="Crowd/Crowd_Bottoms_"+look[1]; break;
                case "Shoes": material="Crowd/Crowd_Shoes_"+look[2]; break;
                case "Hair": material="Crowd/Crowd_Hair_"+look[3]; break;
                case "Body": material="Crowd/Crowd_Skin_"+look[4]; break;
                case "Eyes": material="People_Remy_Eyes"; break;
                case "Eyelashes": material="People_Remy_Eyelashes"; break;
                default: continue;
            }
            Material shared=AssetDatabase.LoadAssetAtPath<Material>(PeopleMaterialFolder+"/"+material+".mat");
            // The eyeballs and lashes sit inside the head: their shadows only speckle the face.
            UnityEngine.Rendering.ShadowCastingMode shadows=part.name=="Eyes"||part.name=="Eyelashes"?UnityEngine.Rendering.ShadowCastingMode.Off:UnityEngine.Rendering.ShadowCastingMode.On;
            if((shared==null||part.sharedMaterial==shared)&&part.shadowCastingMode==shadows)continue;
            Undo.RecordObject(part,"Apply Crowd Look");
            if(shared!=null)part.sharedMaterial=shared;
            part.shadowCastingMode=shadows;
            PrefabUtility.RecordPrefabInstancePropertyModifications(part);
            changed++;
        }
        return changed;
    }

    private void CreateCyclist(Transform parent,string name,Transform[] route,int startIndex,ExteriorTrafficSignal[] signals)
    {
        // Cyclists take the bicycle palettes in creation order (the order ApplyCrowdLooks and Aurelle > Realism > Bicycles use).
        int variant=parent.GetComponentsInChildren<ExteriorCyclistAgent>(true).Length;
        GameObject root=new GameObject(name);
        root.transform.SetParent(parent,false);
        root.transform.position=route[startIndex].position;
        // The procedural city bicycle (ExteriorBicycleBuilder): wheels on their hub axles, crank pivots with their pedals,
        // the saddle with its Sit Point and the handlebar with its grips, as the cyclist agent expects.
        ExteriorBicycleBuilder.Parts bike=ExteriorBicycleBuilder.Build(root.transform,variant);

        GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Presenter/Remy.fbx");
        if(prefab!=null)
        {
            GameObject rider=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);
            rider.name="Cyclist Rider";
            // A 1.7 m rider: the agent moves the Hips bone onto the saddle's Sit Point and poses the rider; the seat post is
            // then fitted to its legs (the knee bends about 30 degrees at the bottom of the stroke).
            rider.transform.localPosition=new Vector3(0f,.27f,-.35f);
            rider.transform.localScale=Vector3.one*.45f;
            Animator anim=rider.GetComponent<Animator>(); if(anim!=null)anim.runtimeAnimatorController=null;
            // The rider's outfit is a saved material assignment (CyclistLooks, applied by ApplyCrowdLooks): a property block would not be saved with the scene.
            ExteriorCyclistAgent agent=root.AddComponent<ExteriorCyclistAgent>();
            agent.Configure(route,4.2f,rider.transform,bike.LeftCrank,bike.RightCrank,signals);
            Debug.Log("[Exterior] "+ExteriorBicycleBuilder.Seat(agent,bike),root);
            agent.SetStartIndex(startIndex+1);
        }
    }

    private void BuildTrafficCar(Transform parent,Vector3 position,Quaternion rotation,int variant,bool parked)
    {
        GameObject car=CreateTrafficCarVisual(parked?"Parked Customer Vehicle":"Traffic Vehicle",parent,variant);
        if(car==null)return;
        car.transform.position=position;
        car.transform.rotation=rotation;
    }

    private GameObject CreateTrafficCarVisual(string name,Transform parent,int variant)
    {
        if(trafficVehiclePrefabs==null || trafficVehiclePrefabs.Length==0)return null;
        GameObject prefab=trafficVehiclePrefabs[Mathf.Abs(variant)%trafficVehiclePrefabs.Length];
        if(prefab==null)return null;
        GameObject root=PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if(root==null)return null;
        root.name=name;
        root.transform.SetParent(parent,false);
        ApplyTrafficPaint(root,variant);
        return root;
    }

    private GameObject BuildTrafficVehiclePrefab(string modelPath,string prefabName,float noseYaw)
    {
        GameObject modelAsset=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if(modelAsset==null)
        {
            Debug.LogWarning("Traffic vehicle model is unavailable: "+modelPath,this);
            return null;
        }
        string path="Assets/Prefabs/ExteriorTraffic/"+prefabName;
        GameObject temporaryRoot=new GameObject(Path.GetFileNameWithoutExtension(prefabName));
        GameObject model=PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
        if(model==null){DestroyImmediate(temporaryRoot);return null;}
        model.name="Imported Vehicle Model";
        model.transform.SetParent(temporaryRoot.transform,false);
        // Turn the model nose-first before measuring, so it is centred on the root after the turn.
        model.transform.localRotation=Quaternion.Euler(0f,noseYaw,0f)*model.transform.localRotation;
        Bounds bounds=CombinedRendererBounds(model);
        if(bounds.size.z<=.001f){DestroyImmediate(temporaryRoot);return null;}
        model.transform.localScale=Vector3.one*(4.5f/bounds.size.z);
        bounds=CombinedRendererBounds(model);
        model.transform.localPosition-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        GameObject saved=PrefabUtility.SaveAsPrefabAsset(temporaryRoot,path);
        DestroyImmediate(temporaryRoot);
        return saved;
    }

    private static Bounds CombinedRendererBounds(GameObject root)
    {
        Renderer[] renderers=root.GetComponentsInChildren<Renderer>(true);
        Bounds bounds=new Bounds(root.transform.position,Vector3.zero);
        bool found=false;
        for(int i=0;i<renderers.Length;i++)
        {
            if(renderers[i]==null)continue;
            if(!found){bounds=renderers[i].bounds;found=true;}
            else bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    private void ApplyTrafficPaint(GameObject root,int variant)
    {
        Color[] colors={new Color(.12f,.23f,.38f),new Color(.55f,.57f,.56f),new Color(.40f,.08f,.055f),new Color(.78f,.77f,.70f)};
        Color tint=colors[Mathf.Abs(variant)%colors.Length];
        Renderer[] renderers=root.GetComponentsInChildren<Renderer>(true);
        MaterialPropertyBlock block=new MaterialPropertyBlock();
        for(int i=0;i<renderers.Length;i++)
        {
            Material[] shared=renderers[i].sharedMaterials;
            bool body=false;
            for(int m=0;m<shared.Length;m++)
            {
                if(shared[m]==null)continue;
                string materialName=shared[m].name.ToLowerInvariant();
                if(materialName.Contains("paint") || materialName.Contains("body151") || materialName.Contains("primary.001")){body=true;break;}
            }
            if(!body)continue;
            renderers[i].GetPropertyBlock(block);
            block.SetColor("_BaseColor",tint);
            block.SetColor("_Color",tint);
            renderers[i].SetPropertyBlock(block);
        }
    }

    private void Tree(Transform parent,Vector3 p,float size)
    {
        Transform root=NewEmpty("Street Tree",parent);
        root.position=p;
        plantedTrees.Add(p);
        Cylinder(root,"Tapered Tree Trunk",new Vector3(0f,2.0f,0f),.48f,4.0f,trunk);
        for(int i=0;i<3;i++)
        {
            float y=3.2f+i*.75f;
            Sphere(root,"Natural Canopy Cluster",new Vector3(Mathf.Sin(i*2.1f)*.35f,y,Mathf.Cos(i*1.7f)*.32f),2.5f*size,(i%2==0)?foliage:foliageLight,false);
        }
    }

    private void TreeIfClear(Transform parent,Vector3 p,float size)
    {
        // Footprints rather than collider bounds, which stay empty while the district root is inactive.
        if(NearBuilding(p,2.5f))return;
        if(OnPavement(p,.6f))return;
        for(int i=0;i<plantedTrees.Count;i++)
            if((plantedTrees[i]-p).sqrMagnitude<3.5f*3.5f)return;
        Tree(parent,p,size);
    }

    // Carriageways with their curbs, sidewalks (walking zone) and the other paved areas, widened by a clearance.
    private static bool OnPavement(Vector3 p,float clearance)
    {
        foreach(float z in EastWestStreets)
        {
            float d=Mathf.Abs(p.z-z);
            if(d<6.8f+clearance || (d>9.8f-clearance && d<13.8f+clearance))return true;
        }
        foreach(float x in NorthSouthStreets)
        {
            float d=Mathf.Abs(p.x-x);
            if(d<6.8f+clearance || (d>9.8f-clearance && d<13.8f+clearance))return true;
        }
        foreach(Rect area in PavedAreas)
            if(p.x>area.xMin-clearance && p.x<area.xMax+clearance && p.z>area.yMin-clearance && p.z<area.yMax+clearance)return true;
        return false;
    }

    private void AddShrub(Transform parent,Vector3 p,float size,Material mat)
    {
        Sphere(parent,"Trimmed Evergreen Shrub",p,new Vector3(size*1.15f,size*.72f,size),mat,false);
    }

    // Poles keep 1.6 m from facades (canopies and balconies stand out up to 1.1 m), and the heads, which reach 1.56 m to +z,
    // keep 0.3 m. A blocked lamp moves 2.5 m along its row, or is left out.
    private void LampIfClear(Transform parent,Vector3 position,Vector3 along)
    {
        foreach(float shift in new[]{0f,2.5f,-2.5f})
        {
            Vector3 p=position+along*shift;
            if(NearBuilding(p,1.6f) || NearBuilding(p+new Vector3(0f,0f,1.56f),.3f))continue;
            StreetLamp(parent,p);
            return;
        }
    }

    private void StreetLamp(Transform parent,Vector3 position)
    {
        Transform root=NewEmpty("Warm LED Street Lantern",parent);
        root.position=position;
        Cylinder(root,"Tapered Galvanized Pole",new Vector3(0f,4.35f,0f),.14f,8.7f,metal);
        Tube(root,new Vector3(0f,8.55f,0f),new Vector3(0f,8.95f,.92f),.08f,metal);
        Box(root,"LED Luminaire",new Vector3(0f,8.89f,.98f),new Vector3(.34f,.12f,1.15f),gold,false);
        GameObject lightObject=new GameObject("Warm Road LED");
        lightObject.transform.SetParent(root,false); lightObject.transform.localPosition=new Vector3(0f,8.75f,.98f);
        Light light=lightObject.AddComponent<Light>(); light.type=LightType.Point; light.color=new Color(1f,.82f,.62f); light.intensity=30f; light.range=17f; light.shadows=LightShadows.None;
    }

    private void Sign(Transform parent,string name,string text,Vector3 groundPosition,Quaternion rotation,float height)
    {
        Transform root=NewEmpty(name,parent); root.position=groundPosition; root.rotation=rotation;
        Cylinder(root,"Road Sign Post",new Vector3(0f,height*.53f,0f),.075f,height*1.06f,metal);
        Box(root,"Enamel Sign Board",new Vector3(0f,height*.80f,0f),new Vector3(1.55f,.76f,.10f),signFace,false);
        CreateText(root,text,new Vector3(0f,height*.80f,.065f),Quaternion.identity,.16f,new Color(.94f,.88f,.72f),TextAlignment.Center,new Vector2(1.38f,.56f));
    }

    private void Bench(Transform parent,Vector3 p)
    {
        Transform root=NewEmpty("Street Bench",parent); root.position=p;
        for(int i=0;i<4;i++) Box(root,"Oak Bench Slat",new Vector3(0f,.48f+i*.09f,((i%2)*.14f)-.08f),new Vector3(1.85f,.075f,.11f),facadeWarm,false);
        for(int side=-1;side<=1;side+=2)
        {
            Box(root,"Bench Metal Leg",new Vector3(side*.70f,.26f,0f),new Vector3(.09f,.52f,.40f),metal,false);
            Box(root,"Bench Back Upright",new Vector3(side*.70f,.82f,-.22f),new Vector3(.085f,.78f,.085f),metal,false);
        }
    }

    private void StreetBin(Transform parent,Vector3 p)
    {
        Cylinder(parent,"City Waste Bin",p+Vector3.up*.48f,.43f,.96f,facadeGraphite);
        Cylinder(parent,"Bin Lid",p+Vector3.up*.99f,.46f,.10f,metal);
        Box(parent,"Bin Slot",p+new Vector3(0f,.69f,.34f),new Vector3(.40f,.13f,.04f),signFace,false);
    }

    private void CreateBikeRack(Transform parent,Vector3 position)
    {
        for(int i=0;i<2;i++)
        {
            Transform rack=NewEmpty("Inverted U Bicycle Rack",parent); rack.position=position+new Vector3((i-.5f)*.8f,0f,0f);
            Tube(rack,new Vector3(-.32f,0f,0f),new Vector3(-.32f,.82f,0f),.035f,metal);
            Tube(rack,new Vector3(-.32f,.82f,0f),new Vector3(.32f,.82f,0f),.035f,metal);
            Tube(rack,new Vector3(.32f,.82f,0f),new Vector3(.32f,0f,0f),.035f,metal);
        }
    }

    private void CreateArrow(Transform parent,Vector3 p,Quaternion rotation,float scale=1f)
    {
        Transform arrow=NewEmpty("Directional Road Arrow",parent); arrow.position=p; arrow.rotation=rotation; arrow.localScale=Vector3.one*scale;
        Box(arrow,"Arrow Stem",new Vector3(0f,.0f,-.30f),new Vector3(.18f,.012f,.85f),roadLine,false);
        Box(arrow,"Arrow Head",new Vector3(0f,.0f,.15f),new Vector3(.72f,.012f,.16f),roadLine,false);
        Box(arrow,"Arrow Left Wing",new Vector3(-.24f,.0f,.30f),new Vector3(.16f,.012f,.50f),roadLine,false,new Vector3(0f,-34f,0f));
        Box(arrow,"Arrow Right Wing",new Vector3(.24f,.0f,.30f),new Vector3(.16f,.012f,.50f),roadLine,false,new Vector3(0f,34f,0f));
    }

    private void ConfigureDaylight()
    {
        const string skyTexturePath="Assets/Environment/HDRI/Kloppenheim_06_PureSky_2K.hdr";
        const string skyMaterialPath=MaterialFolder+"/Kloppenheim_Sunset_Skybox.mat";
        // RenderSettings exposes its scene object only internally; record it so the lighting change can be undone.
        MethodInfo renderSettingsObject=typeof(RenderSettings).GetMethod("GetRenderSettings",BindingFlags.Static|BindingFlags.NonPublic);
        if(renderSettingsObject?.Invoke(null,null) is UnityEngine.Object renderSettings) Undo.RecordObject(renderSettings,"Configure Exterior Daylight");
        Texture2D skyTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(skyTexturePath);
        Shader panoramic=Shader.Find("Skybox/Panoramic");
        if(skyTexture!=null && panoramic!=null)
        {
            Material sky=AssetDatabase.LoadAssetAtPath<Material>(skyMaterialPath);
            if(sky==null)
            {
                sky=new Material(panoramic){name="Kloppenheim Sunset Skybox"};
                AssetDatabase.CreateAsset(sky,skyMaterialPath);
            }
            else sky.shader=panoramic;
            sky.SetTexture("_MainTex",skyTexture);
            if(sky.HasProperty("_Exposure"))sky.SetFloat("_Exposure",.85f);
            if(sky.HasProperty("_Rotation"))sky.SetFloat("_Rotation",0f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox=sky;
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity=.62f;
            RenderSettings.defaultReflectionMode=UnityEngine.Rendering.DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity=.55f;
            DynamicGI.UpdateEnvironment();
        }
        else Debug.LogWarning("Exterior HDRI sky was not assigned. Check the imported HDRI and Skybox/Panoramic shader.",this);
        RenderSettings.fog=true;
        RenderSettings.fogMode=FogMode.ExponentialSquared;
        RenderSettings.fogDensity=.00075f;
        RenderSettings.fogColor=new Color(.64f,.72f,.78f);
        Light sun=GameObject.Find("Exterior Daylight")==null?null:GameObject.Find("Exterior Daylight").GetComponent<Light>();
        if(sun==null)
        {
            GameObject go=new GameObject("Exterior Daylight"); Undo.RegisterCreatedObjectUndo(go,"Build Exterior Environment"); go.transform.SetParent(transform,false); go.transform.rotation=Quaternion.Euler(48f,-30f,0f);
            sun=go.AddComponent<Light>(); sun.type=LightType.Directional; sun.color=new Color(1f,.84f,.68f); sun.intensity=1.12f; sun.shadows=LightShadows.Soft; sun.shadowStrength=.72f;
            go.transform.rotation=Quaternion.Euler(18f,-30f,0f);
        }
        RenderSettings.sun=sun;
    }

    private void BuildSpawn(Transform parent)
    {
        Transform point=NewEmpty("Player Spawn",parent);
        point.position=new Vector3(1f,.04f,29.5f);
        point.rotation=Quaternion.Euler(0f,180f,0f);
        ExteriorSpawnPlacement placement=gameObject.GetComponent<ExteriorSpawnPlacement>();
        if(placement==null) placement=gameObject.AddComponent<ExteriorSpawnPlacement>();
        placement.Configure(point,1.7f);
        // Give the spawn object a visible editor icon without adding a rendered runtime marker.
        EditorGUIUtility.SetIconForObject(point.gameObject,EditorGUIUtility.IconContent("sv_label_0").image as Texture2D);
    }

    // Shared containers only: reuses an existing child of that name. Every individual object uses NewEmpty.
    private Transform Group(string name,Transform parent)
    {
        Transform existing=parent.Find(name);
        if(existing!=null) return existing;
        Transform t=NewEmpty(name,parent);
        return t;
    }

    private Transform NewEmpty(string name,Transform parent)
    {
        GameObject go=new GameObject(name); Undo.RegisterCreatedObjectUndo(go,"Build Exterior Environment");
        go.transform.SetParent(parent,false); return go.transform;
    }

    private GameObject Box(Transform parent,string name,Vector3 position,Vector3 size,Material mat,bool collider=false,Vector3? euler=null,bool isStatic=true)
    {
        GameObject go=GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name=name; go.transform.SetParent(parent,false); go.transform.localPosition=position; go.transform.localScale=size;
        if(euler.HasValue)go.transform.localRotation=Quaternion.Euler(euler.Value);
        Renderer r=go.GetComponent<Renderer>(); r.sharedMaterial=mat;
        if(!collider)DestroyImmediate(go.GetComponent<Collider>());
        if(isStatic)Static(go); return go;
    }

    private GameObject Cylinder(Transform parent,string name,Vector3 position,float diameter,float height,Material mat,Vector3? euler=null,bool isStatic=true)
    {
        GameObject go=GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name=name; go.transform.SetParent(parent,false);
        go.transform.localPosition=position; go.transform.localScale=new Vector3(diameter,height*.5f,diameter);
        if(euler.HasValue)go.transform.localRotation=Quaternion.Euler(euler.Value);
        go.GetComponent<Renderer>().sharedMaterial=mat; DestroyImmediate(go.GetComponent<Collider>()); if(isStatic)Static(go); return go;
    }

    private Renderer Sphere(Transform parent,string name,Vector3 position,float diameter,Material mat,bool collider=false)
    {
        return Sphere(parent,name,position,new Vector3(diameter,diameter,diameter),mat,collider);
    }

    private Renderer Sphere(Transform parent,string name,Vector3 position,Vector3 size,Material mat,bool collider=false)
    {
        GameObject go=GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name=name; go.transform.SetParent(parent,false);
        go.transform.localPosition=position; go.transform.localScale=size; go.GetComponent<Renderer>().sharedMaterial=mat;
        if(!collider)DestroyImmediate(go.GetComponent<Collider>()); Static(go); return go.GetComponent<Renderer>();
    }

    private void Tube(Transform parent,Vector3 a,Vector3 b,float diameter,Material mat,bool isStatic=true)
    {
        Vector3 delta=b-a; GameObject go=GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name="Brushed Steel Tube"; go.transform.SetParent(parent,false);
        go.transform.localPosition=(a+b)*.5f; go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,delta.normalized);
        go.transform.localScale=new Vector3(diameter,delta.magnitude*.5f,diameter); go.GetComponent<Renderer>().sharedMaterial=mat;
        DestroyImmediate(go.GetComponent<Collider>()); if(isStatic)Static(go);
    }

    private void CreateText(Transform parent,string text,Vector3 position,Quaternion rotation,float size,Color color,TextAlignment alignment,Vector2 maxBounds)
    {
        GameObject go=new GameObject("Sign Text - "+text.Replace('\n',' ')); go.transform.SetParent(parent,false);
        // Built-in TextMesh's front is local -Z. Turn it toward the front face of each board.
        go.transform.localPosition=position; go.transform.localRotation=rotation*Quaternion.Euler(0f,180f,0f);
        TextMesh mesh=go.AddComponent<TextMesh>(); mesh.text=text; mesh.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mesh.fontSize=100; mesh.characterSize=size; mesh.anchor=TextAnchor.MiddleCenter; mesh.alignment=alignment; mesh.color=color; mesh.lineSpacing=1.1f;
        MeshRenderer renderer=go.GetComponent<MeshRenderer>(); renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows=false;
        Bounds bounds=renderer.bounds;
        // World bounds: under a parent turned ±90 degrees the text's width lies along z.
        float width=Mathf.Max(bounds.size.x,bounds.size.z);
        if(maxBounds.x>0f && maxBounds.y>0f && width>0.001f && bounds.size.y>0.001f)
        {
            float fit=Mathf.Min(maxBounds.x/width,maxBounds.y/bounds.size.y)*.90f;
            go.transform.localScale=Vector3.one*fit;
        }
    }

    private GameObject MeshObject(string name,Transform parent,Mesh mesh,Material mat,Vector3 position,bool collider,bool isStatic=true)
    {
        GameObject go=new GameObject(name); go.transform.SetParent(parent,false); go.transform.localPosition=position;
        go.AddComponent<MeshFilter>().sharedMesh=mesh; go.AddComponent<MeshRenderer>().sharedMaterial=mat;
        if(collider)go.AddComponent<MeshCollider>().sharedMesh=mesh;
        if(isStatic)Static(go);
        return go;
    }

    private static void Static(GameObject go)
    {
        GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
    }

    private Material Mat(string name,Color color,float metallic,float smooth,string textureName=null,Vector2 tiling=default)
    {
        if(materials.TryGetValue(name,out Material cached))return cached;
        string path=MaterialFolder+"/"+name.Replace(' ','_')+".mat";
        Material mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null)
        {
            Shader shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
            mat=new Material(shader); AssetDatabase.CreateAsset(mat,path);
        }
        mat.color=color;
        if(mat.HasProperty("_BaseColor"))mat.SetColor("_BaseColor",color);
        if(mat.HasProperty("_Metallic"))mat.SetFloat("_Metallic",metallic);
        if(mat.HasProperty("_Smoothness"))mat.SetFloat("_Smoothness",smooth);
        if(textureName!=null)
        {
            Texture2D tex=TextureAsset(textureName);
            if(tex!=null)
            {
                if(mat.HasProperty("_BaseMap"))mat.SetTexture("_BaseMap",tex); else mat.mainTexture=tex;
                Vector2 scale=tiling==default?Vector2.one:tiling;
                if(mat.HasProperty("_BaseMap"))mat.SetTextureScale("_BaseMap",scale); else mat.mainTextureScale=scale;
            }
        }
        EditorUtility.SetDirty(mat); materials[name]=mat; return mat;
    }

    // Concrete tiled to one sidewalk run's length, keeping the 2 m tiles of a full-length sidewalk.
    private Material SidewalkMaterial(float length,bool alongX)
    {
        string name=(alongX?"Sidewalk Concrete ":"Sidewalk Concrete Vertical ")+Mathf.RoundToInt(length)+"m";
        if(materials.TryGetValue(name,out Material cached))return cached;
        Material mat=Mat(name,new Color(.72f,.72f,.69f),.02f,.36f,"ConcreteFine",alongX?new Vector2(length*.5f,2f):new Vector2(2f,length*.5f));
        ApplyPbrMaps(mat,"granular_concrete_diff_2k.jpg","granular_concrete_nor_dx_2k.jpg","granular_concrete_metallic_smoothness.png",.32f,.52f);
        return mat;
    }

    private void ApplyPbrMaps(Material material,string baseColorFile,string normalFile,string smoothnessFile,float normalStrength,float scalarSmoothness)
    {
        string root="Assets/Environment/Textures/PolyHaven/";
        Texture2D baseColor=AssetDatabase.LoadAssetAtPath<Texture2D>(root+baseColorFile);
        Texture2D normal=AssetDatabase.LoadAssetAtPath<Texture2D>(root+normalFile);
        Texture2D smoothness=AssetDatabase.LoadAssetAtPath<Texture2D>(root+"Imported/"+smoothnessFile);
        if(baseColor!=null && material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",baseColor);
        if(normal!=null && material.HasProperty("_BumpMap"))
        {
            material.SetTexture("_BumpMap",normal);
            if(material.HasProperty("_BumpScale"))material.SetFloat("_BumpScale",normalStrength);
            material.EnableKeyword("_NORMALMAP");
        }
        if(smoothness!=null && material.HasProperty("_MetallicGlossMap"))
        {
            material.SetTexture("_MetallicGlossMap",smoothness);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            if(material.HasProperty("_SmoothnessTextureChannel"))material.SetFloat("_SmoothnessTextureChannel",0f);
            if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",0f);
            if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",1f);
        }
        else if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",scalarSmoothness);
        EditorUtility.SetDirty(material);
    }

    private Material Emissive(string name,Color color,float strength)
    {
        Material mat=Mat(name,color,.0f,.6f);
        if(mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",color*strength);
            // A new material defaults to EmissiveIsBlack, and URP's material inspector then strips _EMISSION again.
            mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }

    private Texture2D TextureAsset(string name)
    {
        string path=MaterialFolder+"/Textures/"+name+".png";
        Texture2D existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if(existing!=null)return existing;
        int resolution=256; Texture2D tex=new Texture2D(resolution,resolution,TextureFormat.RGB24,true,true);
        tex.wrapMode=TextureWrapMode.Repeat; tex.filterMode=FilterMode.Trilinear;
        Color32[] pixels=new Color32[resolution*resolution];
        System.Random rng=new System.Random(name.GetHashCode());
        for(int y=0;y<resolution;y++)for(int x=0;x<resolution;x++)
        {
            float broad=Mathf.PerlinNoise(x*.028f,y*.028f);
            float fine=(float)rng.NextDouble();
            float value=.52f+(broad-.5f)*.14f+(fine-.5f)*.08f;
            if(name=="PaverJoint")
            {
                int gx=x%64,gy=y%64;
                if(gx<2||gy<2)value*=.58f;
                else value+=((x/64+y/64)%2==0?.025f:-.02f);
            }
            else if(name=="FacadeStone") value+=((x/32+y/24)%2==0?.035f:-.015f);
            else if(name=="GrassGrain")value+=Mathf.PerlinNoise(x*.09f,y*.09f)*.05f-.025f;
            byte c=(byte)Mathf.Clamp(Mathf.RoundToInt(value*255f),0,255);
            pixels[y*resolution+x]=new Color32(c,c,c,255);
        }
        tex.SetPixels32(pixels);tex.Apply(true,false);
        File.WriteAllBytes(path,tex.EncodeToPNG());DestroyImmediate(tex);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private Mesh BuildGroundMeshAsset()
    {
        Mesh mesh=new Mesh{name="Exterior City Ground Relief"};
        // The ground steps down to ShowroomGroundY exactly under the showroom floor.
        List<float> xs=GroundLines(-210f,210f,ShowroomFloor.xMin,ShowroomFloor.xMax),zs=GroundLines(-130f,270f,ShowroomFloor.yMin,ShowroomFloor.yMax);
        int x0=xs.IndexOf(ShowroomFloor.xMin)+1,x1=xs.IndexOf(ShowroomFloor.xMax),z0=zs.IndexOf(ShowroomFloor.yMin)+1,z1=zs.IndexOf(ShowroomFloor.yMax);
        int columns=xs.Count,rows=zs.Count; Vector3[] v=new Vector3[columns*rows];Vector2[] uv=new Vector2[v.Length];int[] tri=new int[(columns-1)*(rows-1)*6];
        for(int z=0;z<rows;z++)for(int x=0;x<columns;x++)
        {
            float wx=xs[x];float wz=zs[z];
            float edge=Mathf.Max(Mathf.InverseLerp(125f,210f,Mathf.Abs(wx)),Mathf.Max(Mathf.InverseLerp(-75f,-130f,wz),Mathf.InverseLerp(150f,270f,wz)));
            float wave=(Mathf.PerlinNoise((wx+270f)*.018f,(wz+340f)*.021f)-.5f)*2f;
            float y=-.05f+Mathf.Pow(Mathf.Clamp01(edge),2.3f)*Mathf.Max(0f,wave)*1.15f;
            if(x>=x0 && x<=x1 && z>=z0 && z<=z1) y=ShowroomGroundY;
            int idx=z*columns+x;v[idx]=new Vector3(wx,y,wz);uv[idx]=new Vector2((wx+210f)/420f*128f,(wz+130f)/400f*128f);
        }
        int n=0;for(int z=0;z<rows-1;z++)for(int x=0;x<columns-1;x++){int a=z*columns+x,b=a+1,c=a+columns,d=c+1;tri[n++]=a;tri[n++]=c;tri[n++]=b;tri[n++]=b;tri[n++]=c;tri[n++]=d;}
        mesh.vertices=v;mesh.uv=uv;mesh.triangles=tri;mesh.RecalculateNormals();mesh.RecalculateBounds();
        // The step faces would tilt the normals they share with the flat ground around the floor.
        Vector3[] normals=mesh.normals;
        for(int z=z0-1;z<=z1+1;z++)for(int x=x0-1;x<=x1+1;x++)normals[z*columns+x]=Vector3.up;
        mesh.normals=normals;
        return SaveMesh(mesh,"ExteriorCityGround.asset");
    }

    // 33 evenly spaced grid lines plus two copies of each floor edge: the outer copy stays at street level, the inner one drops.
    private static List<float> GroundLines(float from,float to,float edgeMin,float edgeMax)
    {
        var lines=new List<float>{edgeMin,edgeMin,edgeMax,edgeMax};
        for(int i=0;i<=32;i++)lines.Add(Mathf.Lerp(from,to,i/32f));
        lines.Sort();
        return lines;
    }

    private Mesh BuildHillMeshAsset()
    {
        Mesh mesh=new Mesh{name="Distant Rolling City Hills"};int columns=65,rows=17;Vector3[] v=new Vector3[columns*rows];Vector2[] uv=new Vector2[v.Length];int[] tri=new int[(columns-1)*(rows-1)*6];
        for(int z=0;z<rows;z++)for(int x=0;x<columns;x++)
        {
            float wx=Mathf.Lerp(-220f,220f,x/(float)(columns-1));float t=z/(float)(rows-1);float wz=Mathf.Lerp(170f,265f,t);
            float ridge=.52f+Mathf.PerlinNoise((wx+300f)*.012f,2.1f)*.65f+Mathf.Sin(wx*.025f)*.20f;
            float y=-.05f+Mathf.Sin(t*Mathf.PI)*Mathf.Max(0f,ridge)*19f;
            int idx=z*columns+x;v[idx]=new Vector3(wx,y,wz);uv[idx]=new Vector2(x/(float)columns,z/(float)rows);
        }
        int n=0;for(int z=0;z<rows-1;z++)for(int x=0;x<columns-1;x++){int a=z*columns+x,b=a+1,c=a+columns,d=c+1;tri[n++]=a;tri[n++]=c;tri[n++]=b;tri[n++]=b;tri[n++]=c;tri[n++]=d;}
        mesh.vertices=v;mesh.uv=uv;mesh.triangles=tri;mesh.RecalculateNormals();mesh.RecalculateBounds();
        return SaveMesh(mesh,"DistantRollingHills.asset");
    }

    private Mesh BuildSedanMeshAsset(string name,bool cabin)
    {
        Mesh mesh=new Mesh{name=name};
        float[][] ring=cabin?new[]{new[]{.61f,.88f,-1.16f},new[]{.68f,1.02f,-.76f},new[]{.60f,1.55f,-.28f},new[]{.56f,1.57f,.45f},new[]{.64f,1.0f,1.12f}}:
            new[]{new[]{.71f,.25f,-2.0f},new[]{.82f,.36f,-1.68f},new[]{.84f,.55f,-1.12f},new[]{.82f,.62f,.88f},new[]{.69f,.42f,1.70f},new[]{.58f,.28f,1.92f}};
        int sides=8;Vector3[] verts=new Vector3[ring.Length*sides];Vector2[] uv=new Vector2[verts.Length];int[] triangles=new int[(ring.Length-1)*sides*6];
        for(int r=0;r<ring.Length;r++)for(int i=0;i<sides;i++)
        {
            float a=i/(float)sides*Mathf.PI*2f;float x=Mathf.Sin(a)*ring[r][0];float y=ring[r][1]+Mathf.Cos(a)*(cabin ? .32f : .30f);
            int idx=r*sides+i;verts[idx]=new Vector3(x,y,ring[r][2]);uv[idx]=new Vector2(i/(float)sides,r/(float)(ring.Length-1));
        }
        int n=0;for(int r=0;r<ring.Length-1;r++)for(int i=0;i<sides;i++){int a=r*sides+i,b=r*sides+(i+1)%sides,c=(r+1)*sides+i,d=(r+1)*sides+(i+1)%sides;triangles[n++]=a;triangles[n++]=c;triangles[n++]=b;triangles[n++]=b;triangles[n++]=c;triangles[n++]=d;}
        mesh.vertices=verts;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
        return SaveMesh(mesh,name+".asset");
    }

    private Mesh SaveMesh(Mesh generated,string fileName)
    {
        string path=ModelFolder+"/"+fileName;Mesh existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        string assetName=Path.GetFileNameWithoutExtension(fileName);
        if(existing==null){generated.name=assetName;AssetDatabase.CreateAsset(generated,path);return generated;}
        existing.name=assetName;
        EditorUtility.CopySerialized(generated,existing);EditorUtility.SetDirty(existing);DestroyImmediate(generated);return existing;
    }

    private static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        string parent=Path.GetDirectoryName(path).Replace('\\','/');string name=Path.GetFileName(path);
        if(!AssetDatabase.IsValidFolder(parent))EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent,name);
    }
#else
    public void BuildEnvironment() { Debug.LogWarning("Exterior world generation is available from the Unity Editor.", this); }
#endif
}
