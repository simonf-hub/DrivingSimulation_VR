using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BCRoot
{
    // On-screen pedals, bottom right: red brake (Slower, wide and flat) and green accelerator (Faster, tall and narrow),
    // side by side so one thumb rocks between them. Grip ridges make them read as pedals. The brake fills amber while
    // the car is still slowing (tapping again then is a rear-end), as in the prototype. After a fail or a pass the
    // accelerator becomes the Retry / New session button. Drawn on a screen canvas; input is read by
    // MobileDrivingControls from the same rectangles. Not shown in a headset, where the controller buttons are used.
    public sealed partial class DrivingPractice
    {
        static readonly Color BrakeRed = new(.75f, .22f, .17f), GasGreen = new(.12f, .62f, .31f);
        static readonly Color SlowingAmber = new(.95f, .76f, .2f, .85f), RidgeShade = new(0f, 0f, 0f, .16f);

        Canvas pedalCanvas;
        RectTransform brakePedal, gasPedal, aheadButton, slowingFill;
        Image brakeImage, gasImage, brakeRim;
        TextMeshProUGUI gasLabel;
        GameObject gasRidges;
        Sprite roundedSprite;
        Texture2D roundedTexture;
        float brakePress, gasPress, slowFromSpeed;

        void CreatePedals()
        {
            var dmBold = Resources.Load<TMP_FontAsset>("Fonts/DMSans-Bold SDF");
            roundedSprite = RoundedSprite(out roundedTexture);

            var canvasObject = new GameObject("Pedals");
            canvasObject.transform.SetParent(transform, false);
            pedalCanvas = canvasObject.AddComponent<Canvas>();
            pedalCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            pedalCanvas.sortingOrder = 10;

            brakePedal = PedalPanel(canvasObject.transform, "Slower pedal", BrakeRed, out brakeImage);
            brakeRim = Panel(brakePedal, "Still slowing rim", Color.clear);
            brakeRim.fillCenter = false; // just the outline
            brakeRim.pixelsPerUnitMultiplier = 2.5f; // a thin rim
            slowingFill = Panel(brakePedal, "Still slowing", SlowingAmber).rectTransform;
            slowingFill.anchorMin = Vector2.zero;
            slowingFill.anchorMax = new Vector2(1f, 0f);
            slowingFill.pivot = new Vector2(.5f, 0f);
            Ridges(brakePedal, 3);
            PedalLabel(brakePedal, dmBold, "Slower");

            gasPedal = PedalPanel(canvasObject.transform, "Faster pedal", GasGreen, out gasImage);
            gasRidges = Ridges(gasPedal, 5);
            gasLabel = PedalLabel(gasPedal, dmBold, "Faster");

            aheadButton = PedalPanel(canvasObject.transform, "Ahead button", new Color(.07f, .12f, .16f, .85f), out _);
            var aheadLabel = PedalLabel(aheadButton, dmBold, "Ahead").rectTransform;
            aheadLabel.anchorMin = new Vector2(.1f, .18f);
            aheadLabel.anchorMax = new Vector2(.9f, .82f);
        }

        RectTransform PedalPanel(Transform parent, string name, Color color, out Image image)
        {
            image = Panel(parent, name, color);
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, 0f); // presses shrink toward the bottom, like a pedal going down
            return rect;
        }

        Image Panel(Transform parent, string name, Color color)
        {
            var image = new GameObject(name).AddComponent<Image>();
            image.transform.SetParent(parent, false);
            image.sprite = roundedSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return image;
        }

        // Horizontal grip ridges across the upper part of the pedal.
        GameObject Ridges(RectTransform pedal, int count)
        {
            var holder = new GameObject("Grip ridges", typeof(RectTransform));
            holder.transform.SetParent(pedal, false);
            var area = (RectTransform)holder.transform;
            area.anchorMin = new Vector2(.18f, .42f);
            area.anchorMax = new Vector2(.82f, .9f);
            area.offsetMin = area.offsetMax = Vector2.zero;
            for (var i = 0; i < count; i++)
            {
                var ridge = Panel(area, "Ridge", RidgeShade).rectTransform;
                var y = (i + .5f) / count;
                ridge.anchorMin = new Vector2(0f, y);
                ridge.anchorMax = new Vector2(1f, y);
                ridge.sizeDelta = new Vector2(0f, 5f);
            }
            return holder;
        }

        static TextMeshProUGUI PedalLabel(RectTransform pedal, TMP_FontAsset font, string text)
        {
            var label = new GameObject("Label").AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(pedal, false);
            if (font) label.font = font;
            label.text = text;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = 40f;
            label.raycastTarget = false;
            var rect = label.rectTransform;
            rect.anchorMin = new Vector2(.08f, .08f);
            rect.anchorMax = new Vector2(.92f, .34f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return label;
        }

        // Called every frame: layout follows the screen, presses animate, the brake shows "still slowing".
        void UpdatePedals(bool leftTap, bool rightTap)
        {
            if (!pedalCanvas) return;
            pedalCanvas.enabled = !usingHeadset;
            if (usingHeadset) return;

            var (slower, faster, ahead) = MobileDrivingControls.ButtonAreas(Screen.safeArea);
            var driving = result == null;
            if (leftTap && driving) brakePress = .14f;
            if (rightTap) gasPress = .14f;
            brakePress = Mathf.Max(0f, brakePress - Time.unscaledDeltaTime);
            gasPress = Mathf.Max(0f, gasPress - Time.unscaledDeltaTime);

            Place(brakePedal, slower, brakePress > 0f ? .93f : 1f);
            Place(gasPedal, faster, gasPress > 0f ? .93f : 1f);
            Place(aheadButton, ahead, 1f);

            // After a fail or a pass the brake hides and the accelerator becomes Retry (or New session).
            brakePedal.gameObject.SetActive(driving);
            aheadButton.gameObject.SetActive(driving);
            gasRidges.SetActive(driving);
            gasLabel.text = driving ? "Faster" : completedSessionScenario ? "New session" : "Retry";
            gasLabel.rectTransform.anchorMax = new Vector2(.92f, driving ? .34f : .9f);

            // A card is up: taps only close it, so the pedals dim.
            var alpha = coach.Paused ? .45f : 1f;
            brakeImage.color = new Color(BrakeRed.r, BrakeRed.g, BrakeRed.b, alpha);
            gasImage.color = new Color(GasGreen.r, GasGreen.g, GasGreen.b, alpha);

            var slowing = driving && StillSlowing && slowFromSpeed > Speeds[gear];
            var left = slowing ? Mathf.Clamp01((speed - Speeds[gear]) / (slowFromSpeed - Speeds[gear])) : 0f;
            slowingFill.sizeDelta = new Vector2(0f, slower.height * left);
            brakeRim.color = slowing ? SlowingAmber : Color.clear;
        }

        static void Place(RectTransform pedal, Rect area, float press)
        {
            pedal.anchoredPosition = new Vector2(area.center.x, area.y);
            pedal.sizeDelta = area.size * press;
        }

        // A white rounded rectangle (9-sliced), so panels get soft corners without any art asset.
        static Sprite RoundedSprite(out Texture2D texture)
        {
            const int size = 64, radius = 22;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = Mathf.Max(radius - x - .5f, x + .5f - (size - radius), 0f);
                var dy = Mathf.Max(radius - y - .5f, y + .5f - (size - radius), 0f);
                var a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + .5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
        }

        void DestroyPedals()
        {
            if (roundedSprite) Destroy(roundedSprite);
            if (roundedTexture) Destroy(roundedTexture);
        }
    }
}
