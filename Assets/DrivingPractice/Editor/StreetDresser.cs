using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BCRoot.EditorTools
{
    // Dresses the open scenario scene as a BC street (BC Roots > Dress street). The scene's name picks the street type
    // (StreetFor): residential (default), commercial (high street: Kazama's city buildings) or rural (edge of town).
    // Residential cross-section per side, from the road edge out (after City of Vancouver drawings R2.1 / C1.1):
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
        // Layout children that are surfaces or markings, not landmarks. Everything else (vehicles, people, signs,
        // fences, the ball...) is a scenario landmark: nothing tall is placed near it.
        static readonly string[] NotLandmarks = { "Road", "Ground", "Cross street", "Centre marking", RootName };
        const float KeepOutAlong = 3f, KeepOutAcross = 1.5f;

        sealed class Cross { public float zMin, zMax; }

        static System.Random rng;
        static Transform root, groupStrips, groupHouses, groupYards, groupTrees, groupLights, groupPoles;
        static Material concrete, grass, wire;
        static Material[] palettes;
        static float roadHalf, zStart, zEnd, mainEnd, groundHalf;
        static string lastHouse;
        static readonly List<Cross> crosses = new();
        static readonly List<Bounds> keepOut = new();
        static readonly Dictionary<string, GameObject> models = new();
        static readonly List<(string name, float width)> houses = new(); // width along the street when facing it

        static string street;

        static string StreetFor(string scene) => scene switch
        {
            "FlashingRed" or "Ambulance" => "commercial",
            "Expressway" => "rural",
            _ => "residential"
        };

        [MenuItem("BC Roots/Dress street")]
        static void Dress()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Dress street", "Stop Play mode first.", "OK");
                return;
            }
            GameObject layoutRoot = null; // the active scene's, so several scenes can be open at once
            foreach (var r in scene.GetRootGameObjects())
                if (r.name == "Scenario layout") layoutRoot = r;
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
            var spec = SpecFor(scene.name);
            reserved = ReservedFor(spec);
            pullOver = spec?.pullOver != null && spec.pullOver.to > 0f ? (spec.pullOver.from, spec.pullOver.to + .5f) : (0f, 0f);
            street = StreetFor(scene.name);
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

            keepOut.Clear();
            foreach (Transform child in layout)
            {
                if (System.Array.IndexOf(NotLandmarks, child.name) >= 0) continue;
                var b = Bounds(child);
                b.Expand(new Vector3(KeepOutAcross * 2f, 0f, KeepOutAlong * 2f));
                keepOut.Add(b);
            }

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
            if (street == "commercial")
            {
                BuildHighStreet();
                BuildCommercialCrossStreets();
            }
            else if (street == "rural") BuildRural();
            else
            {
                BuildMainRoad();
                BuildCrossStreets();
            }
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
                    foreach (var (a, b) in Without(from, to, side == reserved?.side ? reserved.noSidewalk : default))
                        StripAlongZ(side, roadHalf + CurbWidth + Boulevard, SidewalkWidth, CurbHeight, a, b, concrete, "Sidewalk");
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

                // Front row: lots facing the road (none along a school or park).
                foreach (var (from, to) in MainRoadSpans(Strip + 1f, ClearZone))
                    for (var z = from; z + LotLength <= to + .01f; z += LotLength)
                        if (!IsReserved(side, z, z + LotLength)) FrontLot(side, z + LotLength * .5f, frontLine);

                // Back yards, the back lane, and the next street's houses backing onto it, away from cross streets.
                foreach (var (from, to) in MainRoadSpans(30f, 30f))
                {
                    for (var z = from; z + LotLength <= to + .01f; z += LotLength)
                    {
                        if (IsReserved(side, z, z + LotLength)) continue;
                        var along = z + LotLength * .5f;
                        var extension = InExtension(along); // run-in / run-out: half the trees, nothing across the lane
                        for (var n = BackyardTrees * (extension ? .5f : 1f) + (float)rng.NextDouble() - .5f; n >= 1f; n--)
                            Tree(new Vector3(side * (frontLine + LotDepth * .72f + Jitter(5f)), 0f, along + Jitter(4f)), RandomTree());
                        if (!extension && rng.NextDouble() < .5) // the next street's back yards, across the lane
                            Tree(new Vector3(side * (rearLine + LaneWidth + 3f + (float)rng.NextDouble() * 3f), 0f, along + Jitter(4f)), RandomTree());
                        House(side, along, rearLine + LaneWidth + 6f + Jitter(1f), faceAlongX: true, faceAway: true);
                    }
                    for (var z = from; z + 3.3f <= to; z += 3.4f)
                        if (!IsReserved(side, z, z + 3.4f) && !InExtension(z))
                            Yard("Suburban/fence", new Vector3(side * rearLine, 0f, z + 1.7f), 90f); // rear fences along the lane
                }
            }
            if (reserved?.kind == "school") BuildSchoolGrounds(frontLine);
            if (reserved?.kind == "park") BuildPark(frontLine);
        }

        // ---------- commercial: high street with Kazama's city buildings ----------
        // 0.15 m curb, 3.5 m sidewalk with street trees in grates and lights, buildings right at the back of the
        // sidewalk, a taller second row behind, and low-detail towers further out for a skyline. No power poles
        // (wires run underground downtown). The approach corners stop at the sight-triangle clear zone (a small plaza).

        const float WideSidewalk = 3.5f, CommercialStrip = CurbWidth + WideSidewalk;
        const float CommercialTreeSpacing = 10f, CommercialLightSpacing = 25f;
        static (float from, float to) pullOver; // scenario_spec.json pullOver (ambulance): no sidewalk furniture there

        sealed class CityPiece { public Mesh mesh; public Material[] materials; public Quaternion rotation; public Vector3 scale; }
        static Dictionary<string, CityPiece> cityPieces;

        static void BuildHighStreet()
        {
            var front = roadHalf + CommercialStrip;
            foreach (var side in new[] { -1, 1 })
            {
                foreach (var (from, to) in MainRoadSpans(CommercialStrip))
                {
                    StripAlongZ(side, roadHalf, CurbWidth, CurbHeight, from, to, concrete, "Curb");
                    StripAlongZ(side, roadHalf + CurbWidth, WideSidewalk, CurbHeight, from, to, concrete, "Sidewalk");
                }
                foreach (var (from, to) in MainRoadSpans(CommercialStrip + 1f, ClearZone))
                {
                    for (var z = from + 4f; z < to - 2f; z += CommercialTreeSpacing)
                        StreetTree(new Vector3(side * (roadHalf + 1.3f), 0f, z + Jitter(1f)));
                    for (var z = from + 9f; z < to - 2f; z += CommercialLightSpacing)
                        Light(new Vector3(side * (roadHalf + CurbWidth + .4f), 0f, z), side > 0 ? 90f : -90f);
                    for (var z = from + 6f; z < to - 3f; z += 12f) // café parasols, never where cars pull over
                        if (rng.NextDouble() < .35 && (z < pullOver.from || z > pullOver.to))
                                CityDetail(rng.NextDouble() < .5 ? "detail-parasol-a" : "detail-parasol-b",
                                    new Vector3(side * (roadHalf + CurbWidth + 2.6f), CurbHeight, z), 0f);
                    BuildingRow(side, front, from, to, "front");
                }
                foreach (var (from, to) in MainRoadSpans(30f, 30f))
                    BuildingRow(side, front + 16f, from, to, "tall");
                for (var z = zStart + 10f; z < zEnd; z += 24f)
                    CityBuilding(LowDetail(), side, z + Jitter(6f), 48f + (float)rng.NextDouble() * 18f, faceAway: false);
            }
        }

        // Cross streets on the high street: curb and wide sidewalk, buildings fronting the cross street beyond the
        // main street's corner buildings. On the approach side they stand back to the clear-zone line.
        static void BuildCommercialCrossStreets()
        {
            foreach (var c in crosses)
                foreach (var side in new[] { -1, 1 })
                {
                    var edge = side < 0 ? c.zMin : c.zMax;
                    foreach (var xs in new[] { -1, 1 })
                    {
                        StripAlongX(xs, edge, side, 0f, CurbWidth, CurbHeight, concrete, "Curb");
                        StripAlongX(xs, edge, side, CurbWidth, WideSidewalk, CurbHeight, concrete, "Sidewalk");
                        var near = side < 0 ? ClearZone + .5f : CommercialStrip;
                        for (var x = 30f; x < groundHalf - 6f;)
                        {
                            var width = CityBuilding(FrontPiece(), side, xs * (x + 6f), near, faceAway: false, alongX: true, crossEdge: edge);
                            x += Mathf.Max(width, 6f) + .6f;
                        }
                        if (side > 0)
                            for (var x = roadHalf + CommercialStrip + 4f; x < groundHalf - 2f; x += CommercialTreeSpacing)
                                StreetTree(new Vector3(xs * x, 0f, edge + 1.3f));
                    }
                }
        }

        // Buildings packed along the street between from and to: "front" (shops at the sidewalk, some with awnings)
        // or "tall" (the row behind, taller).
        static void BuildingRow(int side, float near, float from, float to, string row)
        {
            var z = from + .5f;
            while (z < to - 4f)
            {
                var piece = row == "front" ? FrontPiece() : TallPiece();
                var go = MakeCityPiece(piece, groupHouses);
                go.transform.rotation = Quaternion.Euler(0f, side < 0 ? -90f : 90f, 0f) * cityPieces[piece].rotation;
                var b = Bounds(go.transform);
                var width = b.size.z;
                if (z + width > to) { Object.DestroyImmediate(go); break; }
                var nearEdge = side < 0 ? b.max.x : b.min.x;
                go.transform.position += new Vector3(side * near - nearEdge, 0f, z + width * .5f - b.center.z);
                var placed = Bounds(go.transform);
                if (BlocksSightline(placed)) Object.DestroyImmediate(go);
                else if (row == "front" && rng.NextDouble() < .4 && width > 5f)
                    CityDetail(width > 8f ? "detail-overhang-wide" : "detail-awning",
                        new Vector3(side * (near + .05f), 0f, placed.center.z), side < 0 ? -90f : 90f);
                z += width + .3f + (float)rng.NextDouble() * (row == "front" ? .6f : 5f);
            }
        }

        // One city building facing a street; returns its width along that street (0 if dropped).
        static float CityBuilding(string piece, int side, float along, float near, bool faceAway, bool alongX = false, float crossEdge = 0f)
        {
            var go = MakeCityPiece(piece, groupHouses);
            float yaw = alongX ? (side < 0 ? 180f : 0f) : (side < 0 ? -90f : 90f);
            if (faceAway) yaw += 180f;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * cityPieces[piece].rotation;
            var b = Bounds(go.transform);
            float width;
            if (!alongX)
            {
                var nearEdge = side < 0 ? b.max.x : b.min.x;
                go.transform.position += new Vector3(side * near - nearEdge, 0f, along - b.center.z);
                width = b.size.z;
            }
            else
            {
                var nearEdge = side < 0 ? b.max.z : b.min.z;
                go.transform.position += new Vector3(along - b.center.x, 0f, crossEdge + side * near - nearEdge);
                width = b.size.x;
            }
            if (!BlocksSightline(Bounds(go.transform))) return width;
            Object.DestroyImmediate(go);
            return 0f;
        }

        // Kazama's city models (after their FBX rotation) face -z, hence the yaws in CityBuilding/BuildingRow: storefronts face the street.
        static GameObject MakeCityPiece(string name, Transform parent)
        {
            var piece = cityPieces[name];
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = piece.scale;
            go.AddComponent<MeshFilter>().sharedMesh = piece.mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = piece.materials;
            return go;
        }

        static void CityDetail(string name, Vector3 position, float yaw)
        {
            if (!cityPieces.ContainsKey(name)) return;
            var go = MakeCityPiece(name, groupYards);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * cityPieces[name].rotation;
            var b = Bounds(go.transform);
            go.transform.position += new Vector3(position.x - b.center.x, position.y - b.min.y, position.z - b.center.z);
            if (BlocksSightline(Bounds(go.transform))) Object.DestroyImmediate(go);
        }

        static void StreetTree(Vector3 position)
        {
            Box("Tree grate", position + new Vector3(0f, CurbHeight + .005f, 0f), new Vector3(1.1f, .01f, 1.1f), wire);
            Tree(position + new Vector3(0f, CurbHeight, 0f), rng.NextDouble() < .5 ? "tree-large" : "tree-small");
        }

        static string FrontPiece() => PickCity(n => n.StartsWith("building-") && !n.Contains("skyscraper") && !n.StartsWith("building-m") && !n.StartsWith("building-l") && !n.StartsWith("building-n"));
        static string TallPiece() => PickCity(n => n.Contains("skyscraper") || n.StartsWith("building-m") || n.StartsWith("building-l") || n.StartsWith("building-n"));
        static string LowDetail() => PickCity(n => n.StartsWith("low-detail-"));

        static string PickCity(System.Func<string, bool> match)
        {
            var list = new List<string>();
            foreach (var name in cityPieces.Keys) if (match(name)) list.Add(name);
            return list[rng.Next(list.Count)];
        }

        // One of each building in Kazama's city model (Separated Assets/Driving_Sim_Model_City.fbx), by base name.
        static void LoadCityPieces()
        {
            if (cityPieces != null) return;
            cityPieces = new Dictionary<string, CityPiece>();
            var city = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Separated Assets/Driving_Sim_Model_City.fbx");
            // The FBX's own "colormap" materials have no texture; the scenes use these textured copies.
            var colormap = AssetDatabase.LoadAssetAtPath<Material>("Assets/Prefabs/Materials/City_colormap.mat");
            foreach (var r in city.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                var name = System.Text.RegularExpressions.Regex.Replace(mesh.name, @"\d+$", "");
                if (!(name.StartsWith("building-") || name.StartsWith("low-detail-") || name.StartsWith("detail-")) || cityPieces.ContainsKey(name)) continue;
                var materials = r.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                    if (colormap && materials[i] && materials[i].name.StartsWith("colormap")) materials[i] = colormap;
                cityPieces[name] = new CityPiece
                {
                    mesh = mesh, materials = materials,
                    rotation = Quaternion.Inverse(city.transform.rotation) * r.transform.rotation,
                    scale = r.transform.lossyScale
                };
            }
        }

        // ---------- rural: edge of town (80 km/h) ----------
        // Gravel shoulders, grass verges, farm fences, a few houses set well back with gravel driveways and trees
        // around them, a forest edge further out, and a power line along one side. No curbs, sidewalks or lights.

        const float Shoulder = 1.5f, FenceLine = 11f, FarmSpacing = 90f;

        static void BuildRural()
        {
            var gravel = PlaceholderMaterial("Gravel", new Color(.58f, .55f, .5f));
            foreach (var side in new[] { -1, 1 })
            {
                foreach (var (from, to) in MainRoadSpans(0f))
                    StripAlongZ(side, roadHalf, Shoulder, .02f, from, to, gravel, "Gravel shoulder");

                foreach (var (from, to) in MainRoadSpans(0f))
                {
                    // Farm fence, with gaps at driveways. Driveways keep poles and trees off them (keep-out).
                    var drives = new List<float>();
                    for (var z = from + 20f + (side > 0 ? 40f : 0f); z < to - 15f; z += FarmSpacing + Jitter(20f)) drives.Add(z);
                    foreach (var d in drives)
                        keepOut.Add(new Bounds(new Vector3(side * (roadHalf + 25f), 0f, d), new Vector3(50f, 0f, 4f)));
                    for (var z = from; z + 3.4f <= to; z += 3.4f)
                    {
                        var nearDrive = false;
                        foreach (var d in drives) if (Mathf.Abs(z + 1.7f - d) < 3f) nearDrive = true;
                        if (!nearDrive) Yard("Suburban/fence", new Vector3(side * FenceLine, 0f, z + 1.7f), 90f, new Vector3(1f, .45f, .6f));
                    }
                    foreach (var d in drives)
                    {
                        var setback = 24f + (float)rng.NextDouble() * 18f;
                        Box("Gravel driveway", new Vector3(side * (roadHalf + Shoulder + setback * .5f), .01f, d), new Vector3(setback, .02f, 3f), gravel);
                        House(side, d + (rng.NextDouble() < .5 ? -9f : 9f), roadHalf + Shoulder + setback - 4f, faceAlongX: true);
                        for (var n = 0; n < 4; n++)
                            Tree(new Vector3(side * (roadHalf + setback + 10f + Jitter(8f)), 0f, d + Jitter(14f)), RandomTree());
                    }
                    // Forest edge, and the odd lone tree in the fields.
                    for (var z = from; z < to; z += 5f)
                        Tree(new Vector3(side * (56f + Jitter(6f)), 0f, z + Jitter(2f)), RandomTree());
                    for (var z = from + 8f; z < to; z += 30f)
                        if (rng.NextDouble() < .5) Tree(new Vector3(side * (15f + (float)rng.NextDouble() * 20f), 0f, z + Jitter(8f)), "tree-large");
                }
                if (side < 0) PowerLine(-(roadHalf + Shoulder + 2.5f)); // after the driveways, so poles avoid them
            }
        }

        // ---------- reserved sides: school grounds and park (scenario_spec.json noSceneryRight / noSceneryLeft) ----------

        sealed class Reserved
        {
            public int side;
            public float from, to;
            public string kind;
            public (float from, float to) noSidewalk;
        }

        static Reserved reserved;

        // From scenario_spec.json: a playground scenario's no-scenery side becomes a park, any other no-scenery side
        // school grounds. The play-area fence stands in the planted strip, so the park edge has no sidewalk along the
        // play area (a sidewalk would be inside the fence).
        static Reserved ReservedFor(PortSpec.ScenarioSpec spec)
        {
            if (spec == null) return null;
            var left = spec.noSceneryLeft != null && spec.noSceneryLeft.to > 0f;
            var right = spec.noSceneryRight != null && spec.noSceneryRight.to > 0f;
            if (!left && !right) return null;
            var range = left ? spec.noSceneryLeft : spec.noSceneryRight;
            var park = spec.playground != null && spec.playground.to > 0f;
            return new Reserved
            {
                side = left ? -1 : 1, from = range.from, to = range.to, kind = park ? "park" : "school",
                noSidewalk = park ? (spec.playground.from - 2f, spec.playground.to + 2f) : default
            };
        }

        static PortSpec.ScenarioSpec SpecFor(string scene)
        {
            foreach (var s in PortSpec.Current.scenarios)
                if (s.unityEnum == scene) return s;
            return null;
        }

        static bool IsReserved(int side, float from, float to) =>
            reserved != null && side == reserved.side && to > reserved.from && from < reserved.to;

        static IEnumerable<(float from, float to)> Without(float from, float to, (float from, float to) cut)
        {
            if (cut.to <= cut.from || cut.to <= from || cut.from >= to)
            {
                yield return (from, to);
                yield break;
            }
            if (cut.from > from) yield return (from, cut.from);
            if (cut.to < to) yield return (cut.to, to);
        }

        // School grounds: lawn (the ground), a low fence along the back of the sidewalk with a gap at the school's
        // entrance, and a row of trees at the back. Kazama's school building ("School") stays where the scene has it.
        static void BuildSchoolGrounds(float frontLine)
        {
            var side = reserved.side;
            var school = root.parent.Find("School");
            var entrance = school ? Bounds(school).center.z : (reserved.from + reserved.to) * .5f;
            for (var z = reserved.from; z + 3.4f <= reserved.to; z += 3.4f)
            {
                if (Mathf.Abs(z + 1.7f - entrance) < 4f) continue;
                Yard("Suburban/fence", new Vector3(side * (frontLine + .4f), 0f, z + 1.7f), 90f, new Vector3(1f, .5f, 1f));
            }
            for (var z = reserved.from + 3f; z < reserved.to - 2f; z += 9f)
                Tree(new Vector3(side * (36f + Jitter(4f)), 0f, z + Jitter(2f)), "tree-large");
        }

        // Park: open lawn (the ground), trees around the edges away from the child's run to the road, a sand patch,
        // and a simple swing set and slide built from basic shapes (placeholders until the artist's models arrive).
        static void BuildPark(float frontLine)
        {
            var side = reserved.side;
            for (var z = reserved.from + 2f; z < reserved.to - 1f; z += 8f)
                Tree(new Vector3(side * (24f + Jitter(4f)), 0f, z + Jitter(2f)), RandomTree());
            foreach (var z in new[] { 44f, 52f, 114f, 121f })
                Tree(new Vector3(side * (11f + Jitter(3f)), 0f, z + Jitter(1.5f)), RandomTree());

            var sand = PlaceholderMaterial("Sand", new Color(.86f, .78f, .58f));
            var red = PlaceholderMaterial("Play red", new Color(.85f, .22f, .18f));
            var yellow = PlaceholderMaterial("Play yellow", new Color(.98f, .78f, .2f));
            var steel = PlaceholderMaterial("Play steel", new Color(.55f, .58f, .62f));
            var group = new GameObject("Play equipment (placeholder)").transform;
            group.SetParent(groupYards, false);

            // Sand patch under the slide, at the far end of the play area from the child's start.
            Prim(group, PrimitiveType.Cube, "Sand", new Vector3(side * 12.5f, .02f, 100f), new Vector3(8f, .04f, 11f), Vector3.zero, sand);

            // Swing set: two A-frames joined by a top bar, two seats on chains.
            var swing = new Vector3(side * 14f, 0f, 68f);
            foreach (var dz in new[] { -1.9f, 1.9f })
            foreach (var lean in new[] { -12f, 12f })
                Prim(group, PrimitiveType.Cylinder, "Swing leg", swing + new Vector3(Mathf.Sin(lean * Mathf.Deg2Rad) * 1.2f, 1.2f, dz),
                    new Vector3(.08f, 1.24f, .08f), new Vector3(0f, 0f, lean), red);
            Prim(group, PrimitiveType.Cylinder, "Swing bar", swing + new Vector3(0f, 2.4f, 0f), new Vector3(.08f, 1.95f, .08f), new Vector3(90f, 0f, 0f), red);
            foreach (var dz in new[] { -.8f, .8f })
            {
                foreach (var dx in new[] { -.2f, .2f })
                    Prim(group, PrimitiveType.Cylinder, "Chain", swing + new Vector3(dx, 1.6f, dz), new Vector3(.02f, .8f, .02f), Vector3.zero, steel);
                Prim(group, PrimitiveType.Cube, "Seat", swing + new Vector3(0f, .8f, dz), new Vector3(.5f, .05f, .2f), Vector3.zero, yellow);
            }

            // Slide: ladder up to a platform, chute down toward the road side of the sand.
            var slide = new Vector3(side * 13.5f, 0f, 101f);
            foreach (var dz in new[] { -.3f, .3f })
                Prim(group, PrimitiveType.Cube, "Ladder rail", slide + new Vector3(side * -.9f, .9f, dz), new Vector3(.06f, 1.8f, .06f), Vector3.zero, steel);
            for (var y = .3f; y < 1.7f; y += .35f)
                Prim(group, PrimitiveType.Cube, "Rung", slide + new Vector3(side * -.9f, y, 0f), new Vector3(.05f, .04f, .6f), Vector3.zero, steel);
            Prim(group, PrimitiveType.Cube, "Platform", slide + new Vector3(side * -.5f, 1.6f, 0f), new Vector3(.8f, .08f, .8f), Vector3.zero, red);
            var chuteLength = 2.8f;
            Prim(group, PrimitiveType.Cube, "Chute", slide + new Vector3(side * .95f, .85f, 0f), new Vector3(chuteLength, .06f, .6f),
                new Vector3(0f, 0f, side * -32f), yellow);
        }

        static void Prim(Transform parent, PrimitiveType shape, string name, Vector3 position, Vector3 scale, Vector3 euler, Material mat)
        {
            var go = GameObject.CreatePrimitive(shape);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // A plain URP Lit material saved under Art/Placeholders/Materials (created once, then reused).
        static Material PlaceholderMaterial(string name, Color color)
        {
            const string folder = Art + "Placeholders/Materials";
            var path = folder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat) return mat;
            if (!AssetDatabase.IsValidFolder(Art + "Placeholders")) AssetDatabase.CreateFolder(Art.TrimEnd('/'), "Placeholders");
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Art + "Placeholders", "Materials");
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            mat.SetFloat("_Smoothness", .2f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // One front lot: house set back from the sidewalk, a path to the door, sometimes a low hedge along the front.
        static void FrontLot(int side, float along, float frontLine)
        {
            var setback = MinSetback + (float)rng.NextDouble() * (MaxSetback - MinSetback);
            if (!House(side, along, frontLine + setback, faceAlongX: true)) return;
            if (InExtension(along)) return; // run-in / run-out: houses only, no yard details
            for (var x = frontLine + 1.4f; x < frontLine + setback; x += 2.8f)
                Yard("Suburban/path-long", new Vector3(side * x, 0f, along), 90f);
            if (rng.NextDouble() < HedgeChance)
                foreach (var dz in new[] { -3.5f, 3.5f })
                    Yard("Suburban/planter", new Vector3(side * (frontLine + .8f), 0f, along + dz), 90f);
            if (rng.NextDouble() < FrontYardTreeChance) // off to one side of the path, in front of the house
                Tree(new Vector3(side * (frontLine + setback * .5f), 0f, along + (rng.NextDouble() < .5 ? -3.6f : 3.6f)), RandomTree());
        }

        // The run-in and run-out are mostly hidden in sessions (only the first scene's run-in and the last scene's
        // run-out show), so they get houses and trees but not the small yard pieces.
        static bool InExtension(float z) => z < 0f || z > mainEnd;

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

        static void Yard(string model, Vector3 position, float yaw, Vector3? scale = null)
        {
            var go = Spawn(Model(model), groupYards);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = scale ?? Vector3.one;
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
            if (BlocksSightline(Bounds(go.transform))) Object.DestroyImmediate(go);
        }

        // Wooden poles in the planted strip, joined by two sagless wires near the top.
        static void PowerLine(float x)
        {
            var poles = new List<Vector3>();
            foreach (var (from, to) in MainRoadSpans(Strip + 1f, ClearZone))
            {
                poles.Clear();
                for (var z = from + 6f; z < to - 2f; z += PoleSpacing)
                    if (!BlocksSightline(new Bounds(new Vector3(x, 6f, z), new Vector3(1f, 12f, 1f)))) poles.Add(new Vector3(x, 0f, z));
                foreach (var p in poles)
                {
                    var go = Spawn(Model("Streets/electricity-pole-single"), groupPoles);
                    go.transform.localPosition = p;
                    go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // cross-arm across the road
                }
                for (var i = 1; i < poles.Count; i++)
                    if (poles[i].z - poles[i - 1].z < PoleSpacing + 1f) // no wire across a gap where a pole was left out
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

        // Things taller than the ground may not stand in the last ClearZone metres before a cross street, beyond the
        // road edge, nor near a scenario landmark (so no tree hides or frames a hazard differently than the scenario intends).
        static bool BlocksSightline(Bounds b)
        {
            if (b.max.y <= .3f) return false;
            foreach (var c in crosses)
                if (b.max.z > c.zMin - ClearZone && b.min.z < c.zMin && (b.max.x > roadHalf || b.min.x < -roadHalf))
                    return true;
            foreach (var k in keepOut)
                if (b.max.x > k.min.x && b.min.x < k.max.x && b.max.z > k.min.z && b.min.z < k.max.z)
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

            LoadCityPieces();

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
