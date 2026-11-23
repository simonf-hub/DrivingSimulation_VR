using TMPro;
using UnityEngine;

namespace BCRoot
{
    // Windshield head-up display: current speed and the four target speeds.
    // Built like a real HUD: light text floating low on the windshield in the driver's line of sight, no panel behind it.
    // The speed turns amber, then red, when over the posted limit. It is a 3D object in the car, so it stays on the
    // windshield when the driver looks around and also shows in an XR headset.
    // (A speed-limit sign was tried here and removed for now; the limit is still used for the speed colour.)
    public sealed partial class DrivingPractice
    {
        // HUD centre, car-local metres: in front of the driver's eye (x -0.41), just above the dashboard, inside the windshield.
        static readonly Vector3 HudSpot = new(-.41f, 1.158f, 1.05f);
        const float HudUnit = .0125f; // laid out in "HUD centimetres", drawn 25% larger

        static readonly Color HudWhite = new(.88f, .97f, 1f), HudDim = new(.78f, .86f, .92f, .8f);
        static readonly Color HudAmber = new(1f, .74f, .18f), HudRed = new(1f, .3f, .25f), HudTarget = new(.95f, .79f, .3f);

        TextMeshPro hudSpeed;
        TextMeshPro[] hudGears;

        void CreateHud()
        {
            var fredoka = Resources.Load<TMP_FontAsset>("Fonts/Fredoka-SemiBold SDF");
            var dmBold = Resources.Load<TMP_FontAsset>("Fonts/DMSans-Bold SDF");

            var root = new GameObject("Windshield HUD").transform;
            root.SetParent(vehicle, false);
            root.localPosition = HudSpot;
            // Face the driver's eye.
            root.rotation = Quaternion.LookRotation(root.position - driverView.position, vehicle.up);
            root.localScale = Vector3.one * HudUnit;

            hudSpeed = HudText(root, "Speed", fredoka, 44f, new Vector2(0f, .6f), TextAlignmentOptions.Center, HudWhite);
            var unit = HudText(root, "km/h", dmBold, 10f, new Vector2(10.6f, -1.5f), TextAlignmentOptions.Left, HudDim);
            unit.text = "km/h";
            Shadow(hudSpeed);
            Shadow(unit);

            // Target speeds in a small column beside the number, fastest on top like a gear indicator;
            // the one the car is heading to is lit. (Under the number it would sink behind this car's high dashboard.)
            var gears = Speeds;
            hudGears = new TextMeshPro[gears.Length];
            for (var i = 0; i < gears.Length; i++)
            {
                hudGears[i] = HudText(root, "Target " + gears[i], dmBold, 10.5f, new Vector2(9.8f, -3.1f + i * 2f), TextAlignmentOptions.Center, HudDim);
                hudGears[i].text = gears[i].ToString();
                Shadow(hudGears[i]);
            }
        }

        static TextMeshPro HudText(Transform parent, string name, TMP_FontAsset font, float size, Vector2 position, TextAlignmentOptions align, Color color)
        {
            var text = new GameObject(name).AddComponent<TextMeshPro>();
            text.transform.SetParent(parent, false);
            text.transform.localPosition = position;
            text.rectTransform.sizeDelta = new Vector2(14f, size * .14f + 1f);
            if (font) text.font = font;
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        // A soft dark halo behind light HUD text, so it stays readable against bright sky as well as dark road.
        static void Shadow(TextMeshPro text)
        {
            var material = text.fontMaterial; // per-text instance
            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, .55f));
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .6f);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, .4f);
        }

        // The posted limit where the front bumper is: the road's, or a school/playground zone's while inside it.
        float PostedLimit()
        {
            var spec = PortSpec.Current.Scenario(SpecIdFor(authoredScenario.Kind));
            var front = distance + Nose;
            if (spec.zone != null && spec.zone.limitKmh > 0f && front >= spec.zone.from && front <= spec.zone.to) return spec.zone.limitKmh;
            return spec.limitKmh;
        }

        void UpdateHud()
        {
            if (!hudSpeed) return;
            hudSpeed.text = Mathf.RoundToInt(speed).ToString();
            var over = speed > PostedLimit() + PortSpec.Current.engine.speedLimit.toleranceKmh;
            var overLong = Mathf.Max(roadOverTime, zoneOverTime) > .5f;
            hudSpeed.color = !over ? HudWhite : overLong ? HudRed : HudAmber;

            for (var i = 0; i < hudGears.Length; i++)
            {
                var on = i == gear;
                hudGears[i].color = on ? HudTarget : HudDim;
                hudGears[i].fontStyle = on ? FontStyles.Bold | FontStyles.Underline : FontStyles.Normal;
            }
        }
    }
}
