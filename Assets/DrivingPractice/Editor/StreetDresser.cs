using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BCRoot.EditorTools
{
    // Dresses the open scenario scene as a BC residential street (BC Roots > Dress street).
    // Cross-section per side, from the road edge out (after City of Vancouver drawings R2.1 / C1.1):
    // 0.15 m concrete curb, 2 m planted strip (street trees, lights, power poles), 1.8 m sidewalk, then 11 m wide lots
    // about 37 m deep (front yard, house, back yard), a 6 m back lane, and the next street's houses backing onto it.
    // Reads "Road", "Cross street" and "Ground" under "Scenario layout"; never moves scenario landmarks.
    // Everything goes under one "Streetscape" object; running it again rebuilds it from the same seed. Undo works.
    // Streetscape has three parts: "Run-in" (plain street behind the start, z < 0), "Main" (the scenario, 0 to its
    // length) and "Run-out" (plain street past the end). DrivingPractice hides Run-in when the scene joins a session
    // behind another scenario, and Run-out when another scenario follows it, so chained scenes never overlap.
    static class StreetDresser
    {
        const string RootName = "Streetscape";
        const float RunIn = 150f, RunOut = 180f; // the fog hides anything past about 250 m
        const float MarkingSpacing = 6f;
        const string Art = "Assets/DrivingPractice/Art/";
        const float CurbWidth = .15f, CurbHeight = .15f, Boulevard = 2f, SidewalkWidth = 1.8f;
        const float Strip = CurbWidth + Boulevard + SidewalkWidth; // road edge to back of sidewalk
        const float LotLength = 11f, MinSetback = 4f, MaxSetback = 6f, HouseGap = 1.2f;
        const float LotDepth = 36f, LaneWidth = 6f; // front lot line to rear lot line; back lane behind it
        const float TreeSpacing = 8f, LightSpacing = 36f, PoleSpacing = 40f;
        const float HedgeChance = .4f, FrontYardTreeChance = .35f, BackyardTrees = 1.6f; // average per lot
        const float TreeMinScale = .6f, TreeMaxScale = 1.35f; // about 4.5-13.5 m tall
        // Sight triangle: at a stop line the driver must see cross traffic 52 m away, so nothing tall stands in
        // the last ClearZone metres before a cross street (approach side; the player always drives toward +z).
        const float ClearZone = 8f;
        const float PoleHeight = 11.6f;

        static readonly string[] OldDressing = { "Sidewalk", "Tree trunk", "Tree canopy" };

        sealed class Cross { public float zMin, zMax; }

        static System.Random rng;
        static Transform root, groupStrips, groupHouses, groupYards, groupTrees, groupLights, groupPoles;
        static Material concrete, grass, wire;
        static Material[] palettes;
        static float roadHalf, zStart, zEnd, mainEnd, groundHalf;
        static string lastHouse;
        static readonly List<Cross> crosses = new();
        static readonly Dictionary<string, GameObject> models = new();
        static readonly List<(string name, float width)> houses = new(); // width along the street when facing it

        [MenuItem("BC Roots/Dress street (residential)")]
        static void DressResidential()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Dress street", "Stop Play mode first.", "OK");
                return;
            }
            var layoutRoot = GameObject.Find("Scenario layout");
            if (!layoutRoot || layoutRoot.transform.childCount == 0)
            {
                Debug.LogError("Dress street: no 'Scenario layout' in the open scene.");
                return;
            }
            var layout = layoutRoot.transform.GetChild(0);
            var road = layout.Find("Road");
            var ground = layout.Find("Ground");
            if (!road || !ground)
            {
                Debug.LogError("Dress street: 'Road' or 'Ground' missing under the scenario layout.");
                return;
            }

            Undo.SetCurrentGroupName("Dress street");
            var undoGroup = Undo.GetCurrentGroup();
            rng = new System.Random(scene.name.GetHashCode() & 0x7fffffff);
            lastHouse = null;
            LoadAssets();

            var roadBounds = Bounds(road);
            var groundBounds = Bounds(ground);
            roadHalf = roadBounds.max.x;
            mainEnd = roadBounds.max.z; // the scenario runs from z 0 (spawn) to its length
            zStart = -RunIn;
            zEnd = mainEnd + RunOut;
            groundHalf = groundBounds.max.x;

            // Cross streets run the full width of the ground, so cross traffic (spawned up to 52 m out) drives on pavement.
            crosses.Clear();
            foreach (Transform child in layout)
            {
                if (child.name != "Cross street") continue;
                Undo.RecordObject(child, "Dress street");
                var scale = child.localScale;
                var width = Bounds(child).size.x;
                child.localScale = new Vector3(scale.x * (groundHalf * 2f) / width, scale.y, scale.z);
                child.position = new Vector3(0f, child.position.y, child.position.z);
                var b = Bounds(child);
                crosses.Add(new Cross { zMin = b.min.z, zMax = b.max.z });
            }
            crosses.Sort((a, b) => a.zMin.CompareTo(b.zMin));

            // Old placeholder dressing: sidewalks, sphere trees, and the city blocks (they move to the commercial scenes).
            var old = new List<GameObject>();
            foreach (Transform child in layout)
                if (System.Array.IndexOf(OldDressing, child.name) >= 0 || child.name.StartsWith("building-")) old.Add(child.gameObject);
            foreach (var go in old) Undo.DestroyObjectImmediate(go);

            var previous = layout.Find(RootName);
            if (previous) Undo.DestroyObjectImmediate(previous.gameObject);
            root = new GameObject(RootName).transform;
            root.SetParent(layout, false);
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Dress street");
            groupStrips = Group("Curbs, planted strips, sidewalks");
            groupHouses = Group("Houses");
            groupYards = Group("Paths, hedges, fences");
            groupTrees = Group("Trees");
            groupLights = Group("Street lights");
            groupPoles = Group("Power poles");

            BuildExtensions(road, ground, layout);
            BuildMainRoad();
            BuildCrossStreets();
            var summary = $"{groupHouses.childCount} houses, {groupTrees.childCount} trees, {groupYards.childCount} yard pieces, " +
                          $"{groupLights.childCount} lights, {groupPoles.childCount} pole parts";
            SortIntoParts();

            foreach (var t in root.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"Dress street: {summary}, in Run-in / Main / Run-out. Save the scene to keep it.");
        }

        // Road, centre dashes and ground before the scene's own road (which starts 30 m behind the spawn) and past its end.
        static void BuildExtensions(Transform road, Transform ground, Transform layout)
        {
            var roadBounds = Bounds(road);
            var groundBounds = Bounds(ground);
            foreach (var (from, to) in new[] { (zStart, roadBounds.min.z), (roadBounds.max.z, zEnd) })
            {
                Stretch(road, "Extension road", from, to);
                Stretch(ground, "Extension ground", Mathf.Max(from, zStart), Mathf.Min(to, zEnd));
            }

            // Centre dashes in step with the scene's own.
            Transform dash = null;
            foreach (Transform child in layout)
                if (child.name == "Centre marking") { dash = child; break; }
            if (!dash) return;
            var phase = Mathf.Repeat(dash.position.z, MarkingSpacing);
            for (var z = Mathf.Ceil((zStart - phase) / MarkingSpacing) * MarkingSpacing + phase; z <= zEnd; z += MarkingSpacing)
            {
                if (z > roadBounds.min.z - 1f && z < roadBounds.max.z + 1f) continue;
                var copy = Object.Instantiate(dash.gameObject, groupStrips);
                copy.name = "Centre marking";
                copy.transform.position = new Vector3(dash.position.x, dash.position.y, z);
            }
        }

        // A copy of a scene surface (road or ground) covering z from..to.
        static void Stretch(Transform source, string name, float from, float to)
        {
            if (to - from < .1f) return;
            var copy = Object.Instantiate(source.gameObject, groupStrips).transform;
            copy.name = name;
            var size = Bounds(source).size.z;
            copy.localScale = new Vector3(source.lossyScale.x, source.lossyScale.y, source.lossyScale.z * (to - from) / size);
            var b = Bounds(copy);
            copy.position += new Vector3(0f, 0f, (from + to) * .5f - b.center.z);
        }

        // Moves every piece into Run-in / Main / Run-out by where it stands, keeping the category groups inside each.
        static void SortIntoParts()
        {
            var parts = new Dictionary<string, Transform>();
            foreach (var category in new[] { groupStrips, groupHouses, groupYards, groupTrees, groupLights, groupPoles })
            {
                var children = new List<Transform>();
                foreach (Transform child in category) children.Add(child);
                foreach (var child in children)
                {
                    var z = child.position.z;
                    var part = z < 0f ? "Run-in" : z > mainEnd ? "Run-out" : "Main";
                    var key = part + "/" + category.name;
                    if (!parts.TryGetValue(key, out var target))
                    {
                        var partRoot = root.Find(part);
                        if (!partRoot)
                        {
                            partRoot = new GameObject(part).transform;
                            partRoot.SetParent(root, false);
                        }
                        target = new GameObject(category.name).transform;
                        target.SetParent(partRoot, false);
                        parts[key] = target;
                    }
                    child.SetParent(target, true);
                }
                Object.DestroyImmediate(category.gameObject);
            }
        }

        // ---------- main road (runs along z) ----------

        static void BuildMainRoad()
        {
            var frontLine = roadHalf + Strip;        // front lot line (back of sidewalk)
            var rearLine = frontLine + LotDepth;     // rear lot line, where the back lane starts
            foreach (var side in new[] { -1, 1 })
            {
                // Strips stop where the cross street's own strips begin.
                foreach (var (from, to) in MainRoadSpans(Strip))
                {
                    StripAlongZ(side, roadHalf, CurbWidth, CurbHeight, from, to, concrete, "Curb");
                    StripAlongZ(side, roadHalf + CurbWidth, Boulevard, CurbHeight - .02f, from, to, grass, "Planted strip");
                    StripAlongZ(side, roadHalf + CurbWidth + Boulevard, SidewalkWidth, CurbHeight, from, to, concrete, "Sidewalk");
                }

                var treeX = side * (roadHalf + CurbWidth + Boulevard * .5f);
                foreach (var (from, to) in MainRoadSpans(Strip + 1f, ClearZone))
                    for (var z = from + 4f; z < to - 2f; z += TreeSpacing)
                        Tree(new Vector3(treeX, 0f, z + Jitter(1.5f)), rng.NextDouble() < .7 ? "tree-large" : "tree-small");

                // Street lights on the east side, power poles on the west (one utility side, as on many BC streets).
                if (side > 0)
                    foreach (var (from, to) in MainRoadSpans(Strip + 1f, ClearZone))
                        for (var z = from + 10f; z < to - 2f; z += LightSpacing)
                            Light(new Vector3(roadHalf + CurbWidth + .4f, 0f, z), 90f);
                else
                    PowerLine(-(roadHalf + CurbWidth + Boulevard - .4f));

                // Front row: lots facing the road.
                foreach (var (from, to) in MainRoadSpans(Strip + 1f, ClearZone))
                    for (var z = from; z + LotLength <= to + .01f; z += LotLength)
                        FrontLot(side, z + LotLength * .5f, frontLine);

                // Back yards, the back lane, and the next street's houses backing onto it, away from cross streets.
                foreach (var (from, to) in MainRoadSpans(30f, 30f))
                {
                    for (var z = from; z + LotLength <= to + .01f; z += LotLength)
                    {
                        var along = z + LotLength * .5f;
                        for (var n = BackyardTrees + (float)rng.NextDouble() - .5f; n >= 1f; n--)
                            Tree(new Vector3(side * (frontLine + LotDepth * .72f + Jitter(5f)), 0f, along + Jitter(4f)), RandomTree());
                        if (rng.NextDouble() < .5) // the next street's back yards, across the lane
                            Tree(new Vector3(side * (rearLine + LaneWidth + 3f + (float)rng.NextDouble() * 3f), 0f, along + Jitter(4f)), RandomTree());
                        House(side, along, rearLine + LaneWidth + 6f + Jitter(1f), faceAlongX: true, faceAway: true);
                    }
                    for (var z = from; z + 3.3f <= to; z += 3.4f)
                        Yard("Suburban/fence", new Vector3(side * rearLine, 0f, z + 1.7f), 90f); // rear fences along the lane
                }
            }
        }

        // One front lot: house set back from the sidewalk, a path to the door, sometimes a low hedge along the front.
        static void FrontLot(int side, float along, float frontLine)
        {
            var setback = MinSetback + (float)rng.NextDouble() * (MaxSetback - MinSetback);
            if (!House(side, along, frontLine + setback, faceAlongX: true)) return;
            for (var x = frontLine + 1.4f; x < frontLine + setback; x += 2.8f)
                Yard("Suburban/path-long", new Vector3(side * x, 0f, along), 90f);
            if (rng.NextDouble() < HedgeChance)
                foreach (var dz in new[] { -3.5f, 3.5f })
                    Yard("Suburban/planter", new Vector3(side * (frontLine + .8f), 0f, along + dz), 90f);
            if (rng.NextDouble() < FrontYardTreeChance) // off to one side of the path, in front of the house
                Tree(new Vector3(side * (frontLine + setback * .5f), 0f, along + (rng.NextDouble() < .5 ? -3.6f : 3.6f)), RandomTree());
        }

        static string RandomTree() => rng.NextDouble() < .65 ? "tree-large" : "tree-small";

        // The main road's z ranges with cross streets cut out, also split at the scenario's start and end so no piece
        // straddles Run-in / Main / Run-out. gapAfter applies past a cross street, gapBefore (at least the
        // sight-triangle clear zone for tall things) on the approach side.
        static IEnumerable<(float from, float to)> MainRoadSpans(float gapAfter, float gapBefore = -1f)
        {
            if (gapBefore < 0f) gapBefore = gapAfter;
            var from = zStart;
            foreach (var c in crosses)
            {
                foreach (var span in SplitAtParts(from, c.zMin - gapBefore)) yield return span;
                from = c.zMax + gapAfter;
            }
            foreach (var span in SplitAtParts(from, zEnd)) yield return span;
        }

        static IEnumerable<(float from, float to)> SplitAtParts(float from, float to)
        {
            foreach (var cut in new[] { 0f, mainEnd })
            {
                if (cut <= from || cut >= to) continue;
                yield return (from, cut);
                from = cut;
            }
            if (to > from) yield return (from, to);
        }

        // ---------- cross streets (run along x) ----------

        static void BuildCrossStreets()
        {
            foreach (var c in crosses)
            {
                foreach (var side in new[] { -1, 1 })
                {
                    var edge = side < 0 ? c.zMin : c.zMax; // curb line on this side of the cross street
                    var nearSide = side < 0; // the player approaches from -z: the sight-triangle side
                    foreach (var xs in new[] { -1, 1 })
                    {
                        StripAlongX(xs, edge, side, 0f, CurbWidth, CurbHeight, concrete, "Curb");
                        StripAlongX(xs, edge, side, CurbWidth, Boulevard, CurbHeight - .02f, grass, "Planted strip");
                        StripAlongX(xs, edge, side, CurbWidth + Boulevard, SidewalkWidth, CurbHeight, concrete, "Sidewalk");

                        // Lots beyond the main road's corner houses; trees and lights only on the far side.
                        for (var x = 30f; x + LotLength <= groundHalf - 2f; x += LotLength)
                            House(side, xs * (x + LotLength * .5f), Strip + MinSetback + (float)rng.NextDouble() * (MaxSetback - MinSetback),
                                faceAlongX: false, crossEdge: edge);
                        if (nearSide) continue;
                        var treeZ = edge + CurbWidth + Boulevard * .5f;
                        for (var x = roadHalf + Strip + 4f; x < groundHalf - 2f; x += TreeSpacing)
                            Tree(new Vector3(xs * (x + Jitter(1.5f)), 0f, treeZ), rng.NextDouble() < .7 ? "tree-large" : "tree-small");
                        Light(new Vector3(xs * (roadHalf + Strip + 1.5f), 0f, edge + CurbWidth + .4f), 0f);
                    }
                }
            }
        }

        // ---------- pieces ----------

        // A strip along the main road: offset from the road centre line to its inner edge, then width outward.
        static void StripAlongZ(int side, float inner, float width, float height, float from, float to, Material mat, string name)
        {
            if (to - from < .1f) return;
            var x = side * (inner + width * .5f);
            Box(name, new Vector3(x, height * .5f - .05f, (from + to) * .5f), new Vector3(width, height + .1f, to - from), mat);
        }

        // A strip along a cross street, from the main road's edge to the edge of the ground.
        static void StripAlongX(int xs, float edge, int side, float inner, float width, float height, Material mat, string name)
        {
            var z = edge + side * (inner + width * .5f);
            var length = groundHalf - roadHalf;
            Box(name, new Vector3(xs * (roadHalf + length * .5f), height * .5f - .05f, z), new Vector3(length, height + .1f, width), mat);
        }

        static void Box(string name, Vector3 centre, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(groupStrips, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // faceAlongX: a house on the main road; near = distance of its street-side wall from the centre line.
        // Otherwise a house on a cross street (side -1: approach side); near = distance from that side's curb line.
        // faceAway: the house turns its back to this street (it fronts the next street over).
        // Returns false if it was dropped for blocking the sight triangle.
        static bool House(int side, float along, float near, bool faceAlongX, float crossEdge = 0f, bool faceAway = false)
        {
            var go = Spawn(Model("Suburban/" + PickHouse()), groupHouses);
            var palette = palettes[rng.Next(palettes.Length)];
            if (palette) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = palette;

            // Kenney houses face +z: turn to face the street (or away from it).
            float yaw;
            if (faceAlongX) yaw = side < 0 ? 90f : -90f;
            else yaw = side < 0 ? 0f : 180f;
            if (faceAway) yaw += 180f;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localPosition = Vector3.zero;
            var b = Bounds(go.transform); // world space; the scenario layout sits at the origin while editing
            if (faceAlongX)
            {
                var nearEdge = side < 0 ? b.max.x : b.min.x;
                go.transform.position += new Vector3(side * near - nearEdge, 0f, along - b.center.z);
            }
            else
            {
                var nearEdge = side < 0 ? b.max.z : b.min.z;
                go.transform.position += new Vector3(along - b.center.x, 0f, crossEdge + side * near - nearEdge);
            }
            if (!BlocksSightline(Bounds(go.transform))) return true;
            Object.DestroyImmediate(go);
            return false;
        }

        // A house narrow enough for an 11 m lot, never the same model twice in a row.
        static string PickHouse()
        {
            string name;
            do name = houses[rng.Next(houses.Count)].name;
            while (name == lastHouse && houses.Count > 1);
            return lastHouse = name;
        }

        static void Yard(string model, Vector3 position, float yaw)
        {
            var go = Spawn(Model(model), groupYards);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            if (BlocksSightline(Bounds(go.transform))) Object.DestroyImmediate(go);
        }

        static void Tree(Vector3 position, string name)
        {
            var go = Spawn(Model("Suburban/" + name), groupTrees);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, rng.Next(360), 0f);
            // Each tree its own size, a little wider or slimmer than the model too.
            var s = Mathf.Lerp(TreeMinScale, TreeMaxScale, (float)rng.NextDouble());
            var w = s * (.85f + (float)rng.NextDouble() * .35f);
            go.transform.localScale = new Vector3(w, s, w);
            if (BlocksSightline(Bounds(go.transform))) Object.DestroyImmediate(go);
        }

        // Kenney's curved light reaches 2.6 m along its local -z: yaw turns that arm over the road.
        static void Light(Vector3 position, float yaw)
        {
            var go = Spawn(Model("Streets/light-curved"), groupLights);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // Wooden poles in the planted strip, joined by two sagless wires near the top.
        static void PowerLine(float x)
        {
            var poles = new List<Vector3>();
            foreach (var (from, to) in MainRoadSpans(Strip + 1f, ClearZone))
            {
                poles.Clear();
                for (var z = from + 6f; z < to - 2f; z += PoleSpacing) poles.Add(new Vector3(x, 0f, z));
                foreach (var p in poles)
                {
                    var go = Spawn(Model("Streets/electricity-pole-single"), groupPoles);
                    go.transform.localPosition = p;
                    go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // cross-arm across the road
                }
                for (var i = 1; i < poles.Count; i++)
                    foreach (var offset in new[] { -.8f, .8f })
                        Wire(poles[i - 1] + new Vector3(offset, PoleHeight - .6f, 0f), poles[i] + new Vector3(offset, PoleHeight - .6f, 0f));
            }
        }

        static void Wire(Vector3 a, Vector3 b)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "Wire";
            go.transform.SetParent(groupPoles, false);
            go.transform.localPosition = (a + b) * .5f;
            go.transform.localRotation = Quaternion.LookRotation(b - a);
            go.transform.localScale = new Vector3(.03f, .03f, (b - a).magnitude);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = wire;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---------- helpers ----------

        // Things taller than the ground may not stand in the last ClearZone metres before a cross street, beyond the road edge.
        static bool BlocksSightline(Bounds b)
        {
            foreach (var c in crosses)
                if (b.max.z > c.zMin - ClearZone && b.min.z < c.zMin && (b.max.x > roadHalf || b.min.x < -roadHalf) && b.max.y > .3f)
                    return true;
            return false;
        }

        static float Jitter(float amount) => ((float)rng.NextDouble() * 2f - 1f) * amount;

        static Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        static GameObject Spawn(GameObject model, Transform parent) => (GameObject)PrefabUtility.InstantiatePrefab(model, parent);

        static GameObject Model(string path)
        {
            if (models.TryGetValue(path, out var model) && model) return model;
            model = AssetDatabase.LoadAssetAtPath<GameObject>(Art + path + ".fbx");
            if (!model) throw new System.InvalidOperationException("Dress street: missing model " + Art + path + ".fbx");
            models[path] = model;
            return model;
        }

        static void LoadAssets()
        {
            concrete = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Scenarios/Materials/sidewalk.mat");
            grass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Scenarios/Materials/grass.mat");
            wire = AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Scenarios/Materials/asphalt.mat");
            palettes = new[]
            {
                null, // the model's own palette
                AssetDatabase.LoadAssetAtPath<Material>(Art + "Suburban/Materials/House_A.mat"),
                AssetDatabase.LoadAssetAtPath<Material>(Art + "Suburban/Materials/House_B.mat"),
                AssetDatabase.LoadAssetAtPath<Material>(Art + "Suburban/Materials/House_C.mat")
            };

            // Houses that fit an 11 m lot with a gap to the neighbours (width = model x, along the street when facing it).
            houses.Clear();
            foreach (var letter in "abcdefghijklmnopqrstu")
            {
                var name = "building-type-" + letter;
                var go = (GameObject)Object.Instantiate(Model("Suburban/" + name));
                var width = Bounds(go.transform).size.x;
                Object.DestroyImmediate(go);
                if (width <= LotLength - HouseGap) houses.Add((name, width));
            }
        }

        static Bounds Bounds(Transform t)
        {
            var renderers = t.GetComponentsInChildren<Renderer>();
            var b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(t.position, Vector3.zero);
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
