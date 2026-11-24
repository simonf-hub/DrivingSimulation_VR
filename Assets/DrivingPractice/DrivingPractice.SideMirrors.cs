using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BCRoot
{
    // Working side mirrors on the player's sedan. The model's own glass (material "Sedan_zerkalo_", both sides in one
    // mesh) is hidden and a rounded mirror is laid over each side, facing the driver's eye, showing a small camera at
    // the glass: looking back along the car, turned out and down the way drivers set their mirrors, with a sliver of
    // our own car's flank at the inner edge. The passenger side is wider, like the convex mirror on real cars.
    // Phones: small textures, no shadows, 80 m range, and the two cameras take turns (each updates every other frame).
    public sealed partial class DrivingPractice
    {
        const int SideMirrorTextureWidth = 256, SideMirrorTextureHeight = 170;
        const float SideMirrorWidth = .15f, SideMirrorHeight = .10f, SideMirrorCorner = .025f, SideMirrorRim = .01f;
        const float SideMirrorForward = .06f; // toward the driver from the model's glass
        const float SideMirrorOutward = 10f, SideMirrorDown = 3f; // degrees
        const float DriverSideFov = 20f, PassengerSideFov = 27f;  // vertical
        // Centres of the model's mirror glass (player sedan model, car-local metres); left is the driver side.
        static readonly Vector3 LeftMirrorGlass = new(-.886f, .99f, .804f), RightMirrorGlass = new(.899f, .99f, .804f);

        readonly Camera[] sideMirrorCameras = new Camera[2];
        readonly RenderTexture[] sideMirrorTextures = new RenderTexture[2];
        int sideMirrorTurn;

        void CreateSideMirrors()
        {
            foreach (var r in vehicle.GetComponentsInChildren<MeshRenderer>())
                foreach (var m in r.sharedMaterials)
                    if (m && m.name.Contains("zerkalo")) r.enabled = false; // the model's plain grey glass

            var rim = Surface(new Color(.06f, .06f, .06f));
            var sheen = Surface(new Color(.75f, .82f, .9f, .14f));
            MakeTransparent(sheen);
            sheen.SetFloat("_Smoothness", .97f);
            for (var side = 0; side < 2; side++)
            {
                var left = side == 0;
                var texture = new RenderTexture(SideMirrorTextureWidth, SideMirrorTextureHeight, 16) { name = left ? "Left mirror" : "Right mirror" };
                sideMirrorTextures[side] = texture;

                // The mirror surface, facing the driver's eye. The model's housing sits on the driver's side of its glass,
                // so the mirror goes 6 cm toward the driver, in front of the housing, with a dark rim of its own.
                var glassCentre = vehicle.TransformPoint(left ? LeftMirrorGlass : RightMirrorGlass);
                var spot = glassCentre + (driverView.position - glassCentre).normalized * SideMirrorForward;
                var body = new GameObject(left ? "Left side mirror" : "Right side mirror").transform;
                body.SetParent(vehicle, false);
                body.SetPositionAndRotation(spot, Quaternion.LookRotation(spot - driverView.position, vehicle.up));
                var glass = Surface(Color.black, true);
                glass.SetTexture("_EmissionMap", texture);
                glass.SetColor("_EmissionColor", Color.white);
                glass.SetFloat("_Smoothness", .25f);
                SideMirrorPart(body, "Glass", RoundedRect(SideMirrorWidth, SideMirrorHeight, SideMirrorCorner, true), glass, 0f);
                SideMirrorPart(body, "Glass sheen", RoundedRect(SideMirrorWidth, SideMirrorHeight, SideMirrorCorner, false), sheen, -.002f);
                SideMirrorPart(body, "Rim", RoundedRect(SideMirrorWidth + SideMirrorRim * 2f, SideMirrorHeight + SideMirrorRim * 2f,
                    SideMirrorCorner + SideMirrorRim, false), rim, .006f);

                // The camera at the glass, looking back, turned outward and a little down.
                var holder = new GameObject(left ? "Left mirror camera" : "Right mirror camera");
                holder.transform.SetParent(vehicle, false);
                holder.transform.localPosition = left ? LeftMirrorGlass : RightMirrorGlass;
                holder.transform.localRotation = Quaternion.Euler(SideMirrorDown, left ? 180f + SideMirrorOutward : 180f - SideMirrorOutward, 0f);
                var camera = holder.AddComponent<Camera>();
                camera.targetTexture = texture;
                camera.fieldOfView = left ? DriverSideFov : PassengerSideFov;
                camera.nearClipPlane = .35f; // past the mirror's own housing and arm
                camera.farClipPlane = 80f;
                camera.depth = -10f;
                camera.GetUniversalAdditionalCameraData().renderShadows = false;
                sideMirrorCameras[side] = camera;
            }
        }

        void SideMirrorPart(Transform body, string name, Mesh mesh, Material material, float behind)
        {
            mirrorMeshes.Add(mesh); // destroyed with the rear mirror's meshes
            var part = new GameObject(name);
            part.transform.SetParent(body, false);
            part.transform.localPosition = new Vector3(0f, 0f, behind);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        // The two cameras take turns, so the pair costs about one camera per frame.
        void UpdateSideMirrors()
        {
            if (!sideMirrorCameras[0]) return;
            sideMirrorTurn ^= 1;
            sideMirrorCameras[0].enabled = sideMirrorTurn == 0;
            sideMirrorCameras[1].enabled = sideMirrorTurn == 1;
        }

        void DestroySideMirrors()
        {
            for (var side = 0; side < 2; side++)
            {
                if (sideMirrorCameras[side]) sideMirrorCameras[side].targetTexture = null;
                if (!sideMirrorTextures[side]) continue;
                sideMirrorTextures[side].Release();
                Destroy(sideMirrorTextures[side]);
            }
        }
    }
}
