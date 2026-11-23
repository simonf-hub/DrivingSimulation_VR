using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BCRoot
{
    // The beaver's words on screen, in the prototype's style: cream cards with a coloured edge by line type
    // (yellow cue/tip, red alert/fail, green praise/go, blue info), titles in Fredoka, text in DM Sans.
    // - Pause-and-explain card: just above the middle of the screen, never over the rear-view mirror.
    // - Caption: compact, in a low band at the bottom, clear of the pedals; CC adds the full line underneath.
    // - Result card (fail or pass) in the centre; the accelerator pedal turns into the Retry button.
    // - CC button: top right, beside Ahead.
    // Drawn on the pedal canvas; not shown in a headset (the dashboard text carries the words there).
    public sealed partial class DrivingPractice
    {
        static readonly Color Cream = new(1f, .97f, .91f), Navy = new(.08f, .15f, .29f), NavySoft = new(.2f, .27f, .4f);
        static readonly Color HintGrey = new(.42f, .47f, .56f), PillDark = new(.07f, .12f, .16f, .85f);

        RectTransform cardPanel, captionPanel, ccButton;
        Image cardRim, captionRim, ccImage;
        TextMeshProUGUI cardTitle, cardBody, cardHint, captionTitle, captionSub, ccLabel;
        BeaverCoach.Line resultLine; // the fail line on the result card; null after a pass

        static Color KindColor(string kind) => kind switch
        {
            "alert" or "fail" => new Color(.88f, .27f, .18f),
            "praise" or "go" => new Color(.18f, .75f, .36f),
            "info" => new Color(.2f, .4f, .71f),
            _ => new Color(.95f, .76f, .2f) // cue, tip
        };

        void CreateCards()
        {
            var fredoka = Resources.Load<TMP_FontAsset>("Fonts/Fredoka-SemiBold SDF");
            var dmRegular = Resources.Load<TMP_FontAsset>("Fonts/DMSans-Regular SDF");
            var dmBold = Resources.Load<TMP_FontAsset>("Fonts/DMSans-Bold SDF");
            var canvas = pedalCanvas.transform;

            cardPanel = Card(canvas, "Beaver card", out cardRim);
            cardTitle = CardText(cardPanel, "Title", fredoka, Navy, TextAlignmentOptions.TopLeft);
            cardBody = CardText(cardPanel, "Text", dmRegular, NavySoft, TextAlignmentOptions.TopLeft);
            cardHint = CardText(cardPanel, "Hint", dmRegular, HintGrey, TextAlignmentOptions.Top);

            captionPanel = Card(canvas, "Beaver caption", out captionRim);
            captionTitle = CardText(captionPanel, "Caption", fredoka, Navy, TextAlignmentOptions.Top);
            captionSub = CardText(captionPanel, "Subtitle", dmRegular, NavySoft, TextAlignmentOptions.Top);

            ccButton = PedalPanel(canvas, "CC button", PillDark, out ccImage);
            ccLabel = PedalLabel(ccButton, dmBold, "CC");
            ccLabel.rectTransform.anchorMin = new Vector2(.12f, .18f);
            ccLabel.rectTransform.anchorMax = new Vector2(.88f, .82f);
        }

        RectTransform Card(Transform parent, string name, out Image rim)
        {
            var image = Panel(parent, name, Cream);
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, 1f); // positioned by its top edge
            rim = Panel(rect, "Edge", Color.white);
            rim.fillCenter = false;
            rim.pixelsPerUnitMultiplier = 3.2f; // a slim coloured edge
            return rect;
        }

        static TextMeshProUGUI CardText(RectTransform card, string name, TMP_FontAsset font, Color color, TextAlignmentOptions align)
        {
            var text = new GameObject(name).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(card, false);
            if (font) text.font = font;
            text.color = color;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            return text;
        }

        // Lays out title / text / hint top to bottom inside a card of the given width; returns the card height.
        static float Stack(float width, float pad, float gap, params (TextMeshProUGUI text, string content, float size)[] lines)
        {
            var y = pad;
            var inner = width - pad * 2f;
            var first = true;
            foreach (var (text, content, size) in lines)
            {
                var show = !string.IsNullOrEmpty(content);
                text.gameObject.SetActive(show);
                if (!show) continue;
                if (!first) y += gap;
                first = false;
                text.text = content;
                text.fontSize = size;
                var height = text.GetPreferredValues(content, inner, 0f).y;
                text.rectTransform.anchoredPosition = new Vector2(pad, -y);
                text.rectTransform.sizeDelta = new Vector2(inner, height);
                y += height;
            }
            return y + pad;
        }

        // LateUpdate: after this frame's driving and coach changes.
        void LateUpdate()
        {
            if (!cardPanel) return;
            var show = !usingHeadset;
            var safe = Screen.safeArea;
            var unit = Mathf.Min(Screen.width, Screen.height) / 720f;

            ccButton.gameObject.SetActive(show && result == null);
            if (show)
            {
                var cc = MobileDrivingControls.CcArea(safe);
                Place(ccButton, cc, 1f);
                ccImage.color = coach.Subtitles ? HudTarget : PillDark;
                ccLabel.color = coach.Subtitles ? Navy : Color.white;
            }

            // Result card (after a fail or a pass) or the pause card.
            string title = null, body = null, hint = null, kind = null;
            if (result != null)
            {
                if (resultLine != null)
                {
                    (title, body, kind) = (resultLine.Caption, resultLine.Text, "fail");
                    hint = "Tap Retry to try again";
                }
                else if (completedSessionScenario)
                {
                    (title, body, kind) = ($"Session complete  {ScenarioLauncher.CompletedCount}/{ScenarioLauncher.ScenarioCount}", "Well done!", "praise");
                    hint = "Tap New session to start again";
                }
                else
                {
                    (title, body, kind) = ("You passed!", "Well done. You can drive it again for practice.", "praise");
                    hint = "Tap Retry to drive it again";
                }
            }
            else if (coach.Card is { } card)
            {
                (title, body, kind) = (card.Caption, card.Text != card.Caption ? card.Text : null, card.Kind);
                hint = "Tap anywhere to continue";
            }

            cardPanel.gameObject.SetActive(show && title != null);
            if (show && title != null)
            {
                var width = Mathf.Min(safe.width * .62f, 900f * unit);
                var height = Stack(width, 26f * unit, 10f * unit, (cardTitle, title, 40f * unit), (cardBody, body, 27f * unit), (cardHint, hint, 19f * unit));
                // Just above the middle; below the mirror; the result card sits in the middle.
                var top = result != null ? Screen.height / 2f + height / 2f : Mathf.Min(safe.yMax - 8f, Screen.height - MirrorBottomOnScreen() - 10f, Screen.height / 2f + height + 6f);
                cardPanel.anchoredPosition = new Vector2(safe.center.x, top);
                cardPanel.sizeDelta = new Vector2(width, height);
                cardRim.color = KindColor(kind);
            }

            // Caption: only while driving with no card up.
            var caption = result == null && coach.Card == null ? coach.Caption : null;
            captionPanel.gameObject.SetActive(show && caption != null);
            if (show && caption != null)
            {
                var sub = coach.Subtitles && caption.Text != caption.Caption ? caption.Text : null;
                var width = Mathf.Min(safe.width * .5f, 760f * unit);
                var height = Stack(width, 14f * unit, 4f * unit, (captionTitle, caption.Caption, 30f * unit), (captionSub, sub, 21f * unit));
                var bottom = safe.yMin + Mathf.Max(14f, Mathf.Min(safe.width, safe.height) * .04f);
                captionPanel.anchoredPosition = new Vector2(safe.center.x - safe.width * .06f, bottom + height);
                captionPanel.sizeDelta = new Vector2(width, height);
                captionRim.color = KindColor(caption.Kind);
            }
        }
    }
}
