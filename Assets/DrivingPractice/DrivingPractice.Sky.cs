using UnityEngine;
using UnityEngine.Rendering;

namespace BCRoot
{
    // One look for every scenario scene, set in code so the 8 scenes match without editing them: a stylized
    // gradient sky (Resources/Shaders/SkyGradient.shader), a lower, warm afternoon sun behind-left of the driver
    // (who always heads +z), so houses and trees throw readable shadows across the street, sky-matched ambient
    // light, and fog in the horizon colour so distant houses fade into the sky.
    // Lighting settings belong to the active scene, so this runs on Awake and again when a chained scenario
    // becomes the active scene (EnterSessionSection).
    public sealed partial class DrivingPractice
    {
        static readonly Color SkyZenith = new(.33f, .55f, .86f), SkyHorizon = new(.79f, .87f, .93f);
        static readonly Color SunColor = new(1f, .95f, .86f);
        static readonly Quaternion SunRotation = Quaternion.Euler(38f, 40f, 0f);
        const float SunIntensity = 1.3f, SunShadowStrength = .8f;

        static Material skyMaterial;

        void ApplyLook()
        {
            var toSun = -(SunRotation * Vector3.forward);
            if (!skyMaterial)
            {
                var shader = Resources.Load<Shader>("Shaders/SkyGradient");
                if (shader && shader.isSupported) skyMaterial = new Material(shader) { name = "Sky" };
            }
            if (skyMaterial)
            {
                skyMaterial.SetColor("_Zenith", SkyZenith);
                skyMaterial.SetColor("_Horizon", SkyHorizon);
                skyMaterial.SetColor("_SunColor", SunColor);
                skyMaterial.SetVector("_SunDirection", toSun);
                RenderSettings.skybox = skyMaterial;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(SkyZenith, SkyHorizon, .35f) * .95f;
            RenderSettings.ambientEquatorColor = SkyHorizon * .75f;
            RenderSettings.ambientGroundColor = new Color(.3f, .33f, .28f);
            RenderSettings.fogColor = SkyHorizon;

            foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type != LightType.Directional) continue;
                light.transform.rotation = SunRotation;
                light.color = SunColor;
                light.intensity = SunIntensity;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = SunShadowStrength;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSky() => skyMaterial = null;
    }
}
