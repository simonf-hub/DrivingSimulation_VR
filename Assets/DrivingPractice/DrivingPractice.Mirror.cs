using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BCRoot
{
    // Interior rear-view mirror: a rounded mirror fixed at the top centre of the windshield, inside the player's car.
    // A rear-facing camera renders into it, flipped like a real mirror. Because it is a 3D object in the car, it stays put
    // when the driver looks around, and it also shows in an XR headset.
    // In practice, a scenario can set MirrorAlert to flash the frame and show "LOOK BEHIND" (the prototype does this for the ambulance).
    public sealed partial class DrivingPractice
    {
        const int MirrorTextureWidth = 512, MirrorTextureHeight = 148;
        const float MirrorWidth = .25f, MirrorHeight = .072f, MirrorCorner = .028f, MirrorRim = .011f;
        const float MirrorCameraTilt = 4f; // degrees down, so the road behind fills the mirror rather than sky
        // Top centre of the windshield, just under the roof's front edge (player sedan model, car-local metres).
        // The mirror hangs just behind its mount (driver side), so the arm runs up and forward from the mirror's back to the glass.
        // Height is halfway between the earlier spot (1.23) and the headliner's front edge (about 1.38).
        static readonly Vector3 MirrorSpot = new(0f, 1.305f, .85f);
        // Where the mount sticks to the windshield, and the glass's inward normal (sloped windshield of the sedan model:
        // bottom edge at y 1.03 / z 1.39, top edge at y 1.40 / z 0.73).
        // Must stay further forward than the mirror's back, or the arm shows in front of the glass.
        static readonly Vector3 WindshieldMountSpot = new(0f, 1.32f, .873f);
        static readonly Vector3 WindshieldInward = new(0f, -.66f, -.37f);

        Camera mirrorCamera;
        RenderTexture mirrorTexture;
        Transform mirrorBody;
        Material mirrorFrame;
        TextMesh mirrorAlertText;
        readonly List<Mesh> mirrorMeshes = new();
        internal bool MirrorAlert;

        void CreateMirror()
        {
            mirrorTexture = new RenderTexture(MirrorTextureWidth, MirrorTextureHeight, 16) { name = "Rear-view mirror" };

            // The mirror faces the driver's eye.
            var spot = vehicle.TransformPoint(MirrorSpot);
            mirrorBody = new GameObject("Rear-view mirror").transform;
            mirrorBody.SetParent(vehicle, false);
            mirrorBody.SetPositionAndRotation(spot, Quaternion.LookRotation(spot - driverView.position, vehicle.up));

            var glass = Surface(Color.black, true);
            glass.SetTexture("_EmissionMap", mirrorTexture);
            glass.SetColor("_EmissionColor", Color.white);
            glass.SetFloat("_Smoothness", .25f); // glossier washes the image out with sky reflection
            AddMirrorPart("Glass", RoundedRect(MirrorWidth, MirrorHeight, MirrorCorner, true), glass, 0f);
            mirrorFrame = Surface(new Color(.06f, .06f, .06f));
            AddMirrorPart("Frame", RoundedRect(MirrorWidth + MirrorRim * 2f, MirrorHeight + MirrorRim * 2f, MirrorCorner + MirrorRim, false), mirrorFrame, .006f);

            // Glass: a thin glossy, see-through layer in front of the image, so it reads as a mirror and not a screen.
            var sheen = Surface(new Color(.75f, .82f, .9f, .14f));
            MakeTransparent(sheen);
            sheen.SetFloat("_Smoothness", .97f);
            AddMirrorPart("Glass sheen", RoundedRect(MirrorWidth, MirrorHeight, MirrorCorner, false), sheen, -.002f);

            // Mount: a pad stuck to the windshield, and an arm down to the back of the mirror.
            var mountMaterial = Surface(new Color(.1f, .1f, .1f));
            var glassPoint = vehicle.TransformPoint(WindshieldMountSpot);
            var inward = vehicle.TransformDirection(WindshieldInward).normalized;
            var pad = MountPart(PrimitiveType.Cylinder, "Mount base", mountMaterial);
            pad.position = glassPoint + inward * .006f;
            pad.rotation = Quaternion.FromToRotation(Vector3.up, inward);
            pad.localScale = new Vector3(.05f, .005f, .035f); // flat oval pad on the glass
            var armStart = pad.position + inward * .005f;
            var armEnd = mirrorBody.TransformPoint(new Vector3(0f, MirrorHeight * .2f, .014f)); // upper back of the mirror
            var arm = MountPart(PrimitiveType.Cylinder, "Mount arm", mountMaterial);
            arm.position = (armStart + armEnd) * .5f;
            arm.rotation = Quaternion.FromToRotation(Vector3.up, armEnd - armStart);
            arm.localScale = new Vector3(.014f, Vector3.Distance(armStart, armEnd) * .5f, .014f);
            var joint = MountPart(PrimitiveType.Sphere, "Mount joint", mountMaterial);
            joint.position = armEnd;
            joint.localScale = Vector3.one * .022f;

            mirrorAlertText = new GameObject("Look behind").AddComponent<TextMesh>();
            mirrorAlertText.transform.SetParent(mirrorBody, false);
            mirrorAlertText.transform.localPosition = new Vector3(0f, -MirrorHeight * .5f - MirrorRim - .006f, 0f);
            mirrorAlertText.text = "LOOK BEHIND";
            mirrorAlertText.anchor = TextAnchor.UpperCenter;
            mirrorAlertText.alignment = TextAlignment.Center;
            mirrorAlertText.fontStyle = FontStyle.Bold;
            mirrorAlertText.characterSize = .004f;
            mirrorAlertText.fontSize = 48;
            if (display)
            {
                mirrorAlertText.font = display.font;
                mirrorAlertText.GetComponent<MeshRenderer>().sharedMaterial = display.GetComponent<MeshRenderer>().sharedMaterial;
            }
            mirrorAlertText.gameObject.SetActive(false);

            var holder = new GameObject("Rear-view mirror camera");
            holder.transform.SetParent(vehicle, false);
            holder.transform.localPosition = MirrorSpot;
            holder.transform.localRotation = Quaternion.Euler(MirrorCameraTilt, 180f, 0f);
            mirrorCamera = holder.AddComponent<Camera>();
            mirrorCamera.targetTexture = mirrorTexture;
            mirrorCamera.fieldOfView = 16f;
            mirrorCamera.nearClipPlane = 2.9f; // starts just behind our own rear bumper, so the car body never fills the mirror
            mirrorCamera.farClipPlane = 160f;
            mirrorCamera.depth = -10f;
            mirrorCamera.GetUniversalAdditionalCameraData().renderShadows = false; // cheaper on phones
        }

        Transform MountPart(PrimitiveType shape, string name, Material material)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(mirrorBody, true);
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part.transform;
        }

        // URP Lit, switched to alpha-blended transparent.
        static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        void AddMirrorPart(string name, Mesh mesh, Material material, float behind)
        {
            mirrorMeshes.Add(mesh);
            var part = new GameObject(name);
            part.transform.SetParent(mirrorBody, false);
            part.transform.localPosition = new Vector3(0f, 0f, behind);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        // Frame flashes red and white with "LOOK BEHIND" while a scenario asks for it (practice only).
        void UpdateMirrorAlert()
        {
            if (!mirrorFrame) return;
            var alert = MirrorAlert && coach.Coaching && result == null;
            var red = alert && Mathf.Repeat(Time.unscaledTime, .5f) < .25f;
            var color = !alert ? new Color(.06f, .06f, .06f) : red ? new Color(1f, .12f, .12f) : Color.white;
            mirrorFrame.color = color;
            mirrorFrame.SetColor("_EmissionColor", alert ? color * .6f : Color.black);
            if (alert) mirrorFrame.EnableKeyword("_EMISSION");
            if (mirrorAlertText.gameObject.activeSelf != alert) mirrorAlertText.gameObject.SetActive(alert);
            mirrorAlertText.color = red ? new Color(1f, .25f, .25f) : Color.white;
        }

        // The mirror's lowest point on screen (GUI y), so pause cards can sit below it. 0 when it is not in view.
        float MirrorBottomOnScreen()
        {
            var view = driverView ? driverView.GetComponentInChildren<Camera>() : null;
            if (!view || !mirrorBody) return 0f;
            var bottom = 0f;
            var half = new Vector2(MirrorWidth * .5f + MirrorRim, MirrorHeight * .5f + MirrorRim + (MirrorAlert ? .03f : 0f));
            foreach (var corner in new[] { new Vector3(-half.x, -half.y), new Vector3(half.x, -half.y) })
            {
                var p = view.WorldToScreenPoint(mirrorBody.TransformPoint(corner));
                if (p.z <= 0f || p.x < 0f || p.x > Screen.width) return 0f;
                bottom = Mathf.Max(bottom, Screen.height - p.y);
            }
            return bottom < Screen.height * .5f ? bottom : 0f; // only matters when it sits in the upper half
        }

        // A flat rounded rectangle in the XY plane, visible from -Z (like Unity's Quad).
        static Mesh RoundedRect(float width, float height, float radius, bool mirrorU, int cornerSegments = 6)
        {
            var points = new List<Vector3> { Vector3.zero };
            var centres = new[]
            {
                new Vector2(width * .5f - radius, height * .5f - radius), new Vector2(-width * .5f + radius, height * .5f - radius),
                new Vector2(-width * .5f + radius, -height * .5f + radius), new Vector2(width * .5f - radius, -height * .5f + radius)
            };
            for (var c = 0; c < 4; c++)
            for (var s = 0; s <= cornerSegments; s++)
            {
                var a = (c * 90f + s * 90f / cornerSegments) * Mathf.Deg2Rad; // counter-clockwise, as seen by the driver
                points.Add(new Vector3(centres[c].x + Mathf.Cos(a) * radius, centres[c].y + Mathf.Sin(a) * radius));
            }
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            foreach (var p in points)
            {
                var u = p.x / width + .5f;
                uvs.Add(new Vector2(mirrorU ? 1f - u : u, p.y / height + .5f)); // mirrored left-right, like a real mirror
                normals.Add(Vector3.back);
            }
            var triangles = new List<int>();
            for (var i = 1; i < points.Count; i++)
            {
                var next = i == points.Count - 1 ? 1 : i + 1;
                triangles.AddRange(new[] { 0, next, i });
            }
            var mesh = new Mesh { name = "Rounded mirror" };
            mesh.SetVertices(points);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        void DestroyMirror()
        {
            if (mirrorCamera) mirrorCamera.targetTexture = null;
            if (mirrorTexture)
            {
                mirrorTexture.Release();
                Destroy(mirrorTexture);
            }
            foreach (var mesh in mirrorMeshes) Destroy(mesh);
        }
    }
}
