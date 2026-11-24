using UnityEngine;
using UnityEngine.Rendering;

namespace BCRoot
{
    // Mountains on the horizon, like the North Shore mountains seen from Vancouver: tallest ahead (+z, the way the
    // player drives), lower to the sides and behind. Two low-poly rings (forested foothills, then taller ranges with
    // snow) sit 360-395 m out, inside the camera's 420 m far plane, and follow the camera so they never get closer.
    // The scene's exponential fog would hide them at that distance, so they use a fog-free unlit shader
    // (Resources/Shaders/BackdropUnlit.shader) with colours blended toward the fog colour by hand.
    // Built once and shared: chained scenario scenes reuse the same mesh; whichever scene is running re-creates the
    // object if its scene was unloaded.
    public sealed partial class DrivingPractice
    {
        const int BackdropSegments = 240;
        const float BackdropBase = -45f; // below the horizon, so no gap shows under the foothills

        const int CloudCount = 16;
        const float CloudRadius = 320f, CloudDrift = .25f; // degrees per second around the camera

        static Transform backdrop, clouds;
        static Mesh backdropMesh, cloudMesh;
        static Material backdropMaterial;

        void UpdateBackdrop()
        {
            var view = Camera.main;
            if (!view) return;
            if (!backdrop && !CreateBackdrop()) return;
            var p = view.transform.position;
            backdrop.position = new Vector3(p.x, 0f, p.z);
            clouds.localRotation = Quaternion.Euler(0f, Time.time * CloudDrift, 0f);
        }

        static bool CreateBackdrop()
        {
            if (!backdropMaterial)
            {
                var shader = Resources.Load<Shader>("Shaders/BackdropUnlit");
                if (!shader || !shader.isSupported) return false;
                backdropMaterial = new Material(shader) { name = "Mountain backdrop" };
            }
            if (!backdropMesh) backdropMesh = BuildMountains(RenderSettings.fogColor);
            if (!cloudMesh) cloudMesh = BuildClouds(RenderSettings.fogColor, -(SunRotation * Vector3.forward));
            backdrop = BackdropObject("Mountain backdrop", backdropMesh, null);
            clouds = BackdropObject("Clouds", cloudMesh, backdrop);
            return true;
        }

        static Transform BackdropObject(string name, Mesh mesh, Transform parent)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = backdropMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        // Low-poly clouds: each a cluster of flattened, flat-shaded icosahedrons, brighter on top and toward the
        // sun, greyer underneath, sitting above the mountains (15-32 degrees up) inside the far plane.
        static Mesh BuildClouds(Color haze, Vector3 toSun)
        {
            var t = (1f + Mathf.Sqrt(5f)) * .5f;
            var ico = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (var i = 0; i < ico.Length; i++) ico[i] = ico[i].normalized;
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            var lit = Color.Lerp(Color.white, haze, .1f);
            var shade = Color.Lerp(new Color(.72f, .78f, .87f), haze, .2f);

            var vertices = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color>();
            var random = new System.Random(7);
            float Range(float a, float b) => a + (float)random.NextDouble() * (b - a);
            for (var c = 0; c < CloudCount; c++)
            {
                var angle = (c + Range(-.3f, .3f)) * Mathf.PI * 2f / CloudCount;
                var centre = new Vector3(Mathf.Sin(angle) * CloudRadius, Range(90f, 190f), Mathf.Cos(angle) * CloudRadius);
                var along = new Vector3(Mathf.Cos(angle), 0f, -Mathf.Sin(angle)); // around the ring
                var blobs = random.Next(4, 8);
                for (var b = 0; b < blobs; b++)
                {
                    var middle = 1f - Mathf.Abs(b - (blobs - 1) * .5f) / blobs; // bigger in the middle
                    var size = new Vector3(Range(18f, 30f), Range(8f, 13f) * (.6f + middle * .6f), Range(14f, 24f));
                    var offset = along * ((b - (blobs - 1) * .5f) * Range(14f, 20f)) + new Vector3(0f, Range(-2f, 3f) * middle, Range(-8f, 8f));
                    var turn = Quaternion.Euler(0f, angle * Mathf.Rad2Deg + Range(-20f, 20f), 0f);
                    for (var f = 0; f < faces.Length; f += 3)
                    {
                        var a = centre + offset + turn * Vector3.Scale(ico[faces[f]], size);
                        var bb = centre + offset + turn * Vector3.Scale(ico[faces[f + 1]], size);
                        var cc = centre + offset + turn * Vector3.Scale(ico[faces[f + 2]], size);
                        var normal = Vector3.Cross(bb - a, cc - a).normalized;
                        var light = Mathf.Clamp01(.55f + .45f * normal.y + .2f * Vector3.Dot(normal, toSun));
                        var color = Color.Lerp(shade, lit, light).linear;
                        vertices.Add(a); vertices.Add(bb); vertices.Add(cc);
                        colors.Add(color); colors.Add(color); colors.Add(color);
                    }
                }
            }
            var triangles = new int[vertices.Count];
            for (var i = 0; i < triangles.Length; i++) triangles[i] = i;
            var mesh = new Mesh { name = "Clouds" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(800f, 500f, 800f));
            return mesh;
        }

        static Mesh BuildMountains(Color haze)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color>();
            var triangles = new System.Collections.Generic.List<int>();
            var random = new System.Random(52); // same skyline every run

            // Far ranges: up to about 95 m at 395 m (about 13 degrees up) ahead; snow above 70% of the tallest peak.
            Ring(vertices, colors, triangles, random, 395f, 35f, 95f, 18f,
                Color.Lerp(haze, new Color(.22f, .33f, .4f), .45f), Color.Lerp(haze, new Color(.42f, .5f, .58f), .35f),
                Color.Lerp(haze, new Color(.97f, .98f, 1f), .8f), .7f);
            // Foothills: lower, darker (forest), a little closer.
            Ring(vertices, colors, triangles, random, 360f, 14f, 38f, 10f,
                Color.Lerp(haze, new Color(.13f, .26f, .22f), .6f), Color.Lerp(haze, new Color(.2f, .34f, .3f), .5f),
                Color.clear, 2f);

            // Mesh colours are read as linear in a linear-colour project; these were picked in gamma.
            for (var i = 0; i < colors.Count; i++) colors[i] = colors[i].linear;
            var mesh = new Mesh { name = "Mountains" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(900f, 300f, 900f));
            return mesh;
        }

        // One ring of peaks. Each column: base, mid-slope, peak (peaks alternate high/low for a low-poly ridge).
        static void Ring(System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<Color> colors,
            System.Collections.Generic.List<int> triangles, System.Random random, float radius, float behindHeight,
            float aheadHeight, float roughness, Color foot, Color slope, Color snow, float snowLine)
        {
            var start = vertices.Count;
            var phase = new float[4];
            for (var i = 0; i < phase.Length; i++) phase[i] = (float)random.NextDouble() * Mathf.PI * 2f;
            for (var s = 0; s <= BackdropSegments; s++)
            {
                var angle = s * Mathf.PI * 2f / BackdropSegments; // 0 = straight ahead (+z)
                var ahead = .5f + .5f * Mathf.Cos(angle);
                // Ridged waves (seamless around the ring: whole numbers of waves) plus a little jitter.
                var ridge = .55f * Mathf.Abs(Mathf.Sin(angle * 3f + phase[0])) + .3f * Mathf.Abs(Mathf.Sin(angle * 7f + phase[1]))
                          + .15f * Mathf.Abs(Mathf.Sin(angle * 13f + phase[2]));
                var height = Mathf.Lerp(behindHeight, aheadHeight, ahead * ahead) * (.45f + .55f * ridge);
                if (s % 2 == 1) height -= (float)random.NextDouble() * roughness * .5f;
                if (s == BackdropSegments) height = vertices[start + 2].y; // close the ring
                var r = radius + Mathf.Sin(angle * 5f + phase[3]) * 6f;
                var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var peakTall = height / aheadHeight;
                vertices.Add(dir * r + Vector3.up * BackdropBase);
                vertices.Add(dir * (r - 4f) + Vector3.up * (height * .45f));
                vertices.Add(dir * (r - 8f) + Vector3.up * height);
                colors.Add(foot);
                colors.Add(Color.Lerp(foot, slope, .6f));
                colors.Add(peakTall > snowLine ? snow : slope);
            }
            for (var s = 0; s < BackdropSegments; s++)
            {
                var a = start + s * 3;
                var b = a + 3;
                for (var row = 0; row < 2; row++)
                {
                    triangles.Add(a + row); triangles.Add(a + row + 1); triangles.Add(b + row);
                    triangles.Add(b + row); triangles.Add(a + row + 1); triangles.Add(b + row + 1);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetBackdrop()
        {
            backdrop = clouds = null;
            backdropMesh = cloudMesh = null; // rebuilt on the next Play (picks up fog colour changes)
            backdropMaterial = null;
        }
    }
}
