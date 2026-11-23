using System.Collections.Generic;
using UnityEngine;

namespace BCRoot
{
    // Interior roof for the player's car. The sedan model has a roof, but it is single-sided (faces the sky), so from the
    // driver's seat it is invisible and the sky shows through. This adds what a real sedan has inside: a fabric headliner
    // following the roof's curve, two sun visors folded up at the top of the windshield, and a dome light.
    // shortcut: the roof shape below was sampled from Driving_Sim_Model_Sedan.fbx (Plane01 / Plane49); if the player car model
    // changes, re-sample it, or ask the artist for a model with an interior headliner and drop this file.
    public sealed partial class DrivingPractice
    {
        // Roof height along the car's centre line, and at the roof's side edge (|x| = RoofHalfWidth), by z (car-local metres).
        static readonly float[] RoofZ = { -.76f, -.56f, -.27f, .02f, .30f, .65f, .745f };
        static readonly float[] RoofCentreY = { 1.44f, 1.455f, 1.47f, 1.46f, 1.45f, 1.41f, 1.40f };
        static readonly float[] RoofEdgeY = { 1.33f, 1.34f, 1.39f, 1.40f, 1.38f, 1.32f, 1.33f };
        const float RoofHalfWidth = .58f, HeadlinerDrop = .02f;

        Mesh headlinerMesh;

        static float Interpolate(float[] values, float z)
        {
            if (z <= RoofZ[0]) return values[0];
            for (var i = 1; i < RoofZ.Length; i++)
                if (z <= RoofZ[i]) return Mathf.Lerp(values[i - 1], values[i], Mathf.InverseLerp(RoofZ[i - 1], RoofZ[i], z));
            return values[^1];
        }

        // Underside of the roof at (x, z): the edge height plus a crown that peaks at the centre line.
        static float HeadlinerY(float x, float z)
        {
            var across = Mathf.Clamp01(Mathf.Abs(x) / RoofHalfWidth);
            var edge = Interpolate(RoofEdgeY, z);
            return edge + (Interpolate(RoofCentreY, z) - edge) * (1f - across * across) - HeadlinerDrop;
        }

        void CreateInteriorRoof()
        {
            // Black cloth headliner, matching this car's dark interior (black pillars, grey dash). Charcoal rather than pure black
            // so the roof's curve and the visors still read. The sun never reaches the underside of the roof, so a little
            // emission stands in for the light bouncing around a real cabin.
            var fabricColor = new Color(.07f, .07f, .075f);
            var fabric = Surface(fabricColor);
            fabric.SetFloat("_Smoothness", .03f);
            fabric.SetColor("_EmissionColor", fabricColor * .15f);
            fabric.EnableKeyword("_EMISSION");
            var trimColor = new Color(.14f, .14f, .15f);
            var trim = Surface(trimColor);
            trim.SetFloat("_Smoothness", .2f);
            trim.SetColor("_EmissionColor", trimColor * .15f);
            trim.EnableKeyword("_EMISSION");

            var roof = new GameObject("Interior roof").transform;
            roof.SetParent(vehicle, false);

            // Headliner: a curved sheet just under the roof, facing down into the cabin.
            headlinerMesh = BuildHeadliner(14, 20);
            var liner = new GameObject("Headliner");
            liner.transform.SetParent(roof, false);
            liner.AddComponent<MeshFilter>().sharedMesh = headlinerMesh;
            liner.AddComponent<MeshRenderer>().sharedMaterial = fabric;

            // Sun visors, folded up flat against the headliner at the top of the windshield (about 35 x 15 cm each).
            foreach (var x in new[] { -.28f, .28f })
            {
                var visor = RoofPart(roof, PrimitiveType.Cube, x < 0f ? "Sun visor (driver)" : "Sun visor (passenger)", fabric);
                var z = .64f;
                visor.localPosition = new Vector3(x, HeadlinerY(x, z) - .012f, z);
                visor.localRotation = Quaternion.Euler(Mathf.Atan2(HeadlinerY(x, z + .05f) - HeadlinerY(x, z - .05f), .1f) * -Mathf.Rad2Deg, 0f, 0f);
                visor.localScale = new Vector3(.34f, .018f, .15f);
                var clip = RoofPart(roof, PrimitiveType.Cube, "Visor clip", trim);
                clip.localPosition = new Vector3(x - Mathf.Sign(x) * .19f, HeadlinerY(x, z) - .008f, z - .03f);
                clip.localScale = new Vector3(.025f, .012f, .03f);
            }

            // Dome light, centre of the roof.
            var dome = RoofPart(roof, PrimitiveType.Cube, "Dome light", trim);
            dome.localPosition = new Vector3(0f, HeadlinerY(0f, .05f) - .008f, .05f);
            dome.localScale = new Vector3(.16f, .016f, .09f);
            var lens = RoofPart(roof, PrimitiveType.Cube, "Dome light lens", Surface(new Color(.93f, .92f, .86f)));
            lens.localPosition = dome.localPosition - new Vector3(0f, .006f, 0f);
            lens.localScale = new Vector3(.12f, .008f, .06f);
        }

        static Transform RoofPart(Transform parent, PrimitiveType shape, string name, Material material)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part.transform;
        }

        static Mesh BuildHeadliner(int columns, int rows)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (var r = 0; r <= rows; r++)
            for (var c = 0; c <= columns; c++)
            {
                var x = Mathf.Lerp(-RoofHalfWidth, RoofHalfWidth, c / (float)columns);
                var z = Mathf.Lerp(RoofZ[0], RoofZ[^1], r / (float)rows);
                vertices.Add(new Vector3(x, HeadlinerY(x, z), z));
                uvs.Add(new Vector2(c / (float)columns, r / (float)rows));
            }
            var triangles = new List<int>();
            for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++)
            {
                var i = r * (columns + 1) + c;
                // Wound to face down, into the cabin.
                triangles.AddRange(new[] { i, i + 1, i + columns + 1, i + 1, i + columns + 2, i + columns + 1 });
            }
            var mesh = new Mesh { name = "Headliner" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
