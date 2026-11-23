using UnityEngine;
using UnityEngine.InputSystem;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace BCRoot
{
    internal sealed class MobileDrivingControls
    {
        static readonly Color normalColor = new(.07f, .12f, .16f, .92f);
        static readonly Color retryColor = new(.08f, .32f, .40f, .98f);

        GUIStyle buttonStyle;
        bool clickLeft, clickRight, clickAhead;
        Touchscreen lastScreen;
        int[] activeTouchIds;

        internal static bool UsesTouch => Application.isMobilePlatform || Touchscreen.current != null;
        internal static bool Visible => UsesTouch || Application.isEditor;

        internal (bool LeftTap, bool RightTap, bool AheadTap, Vector2 LookDelta) Read()
        {
            var left = clickLeft;
            var right = clickRight;
            var ahead = clickAhead;
            clickLeft = clickRight = clickAhead = false;
            var lookDelta = Vector2.zero;
            var hasLookFinger = false;
            if (Touchscreen.current is not { } screen) return (left, right, ahead, lookDelta);

            var (leftArea, rightArea, aheadArea) = ButtonAreas(Screen.safeArea);
            if (screen != lastScreen)
            {
                lastScreen = screen;
                activeTouchIds = new int[screen.touches.Count];
            }

            for (var i = 0; i < screen.touches.Count; i++)
            {
                var touch = screen.touches[i];
                var phase = touch.phase.ReadValue();
                if (phase is TouchPhase.None or TouchPhase.Ended or TouchPhase.Canceled)
                {
                    activeTouchIds[i] = 0;
                    continue;
                }

                var id = touch.touchId.ReadValue();
                var isNew = phase == TouchPhase.Began && activeTouchIds[i] != id;
                activeTouchIds[i] = id;
                var start = touch.startPosition.ReadValue();
                if (leftArea.Contains(start)) left |= isNew;
                else if (rightArea.Contains(start)) right |= isNew;
                else if (aheadArea.Contains(start)) ahead |= isNew;
                else if (!hasLookFinger && !isNew)
                {
                    lookDelta = touch.delta.ReadValue();
                    hasLookFinger = true;
                }
            }
            return (left, right, ahead, lookDelta);
        }

        internal void Draw(bool showLeft, string rightLabel)
        {
            var (left, right, ahead) = ButtonAreas(Screen.safeArea);
            buttonStyle ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            buttonStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(left.height * .31f), 18, 56);

            var color = GUI.backgroundColor;
            if (showLeft)
            {
                GUI.backgroundColor = normalColor;
                clickLeft |= DrawButton(ToGui(left), "−  Slow down");
                if (ahead.width > 0f) clickAhead |= DrawButton(ToGui(ahead), "Ahead");
            }
            GUI.backgroundColor = showLeft ? normalColor : retryColor;
            if (rightLabel != null) clickRight |= DrawButton(ToGui(right), rightLabel);
            GUI.backgroundColor = color;
        }

        bool DrawButton(Rect rect, string label)
        {
            // Read touches only through the Input System, so a simulated mouse click cannot shift twice.
            if (!UsesTouch) return GUI.Button(rect, label, buttonStyle);
            GUI.Box(rect, label, buttonStyle);
            return false;
        }

        static Rect ToGui(Rect rect) => new(rect.x, Screen.height - rect.yMax, rect.width, rect.height);

        internal static (Rect Left, Rect Right, Rect Ahead) ButtonAreas(Rect safeArea)
        {
            var shortSide = Mathf.Min(safeArea.width, safeArea.height);
            var height = Mathf.Clamp(shortSide * .15f, 54f, 180f);
            var margin = Mathf.Max(12f, shortSide * .035f);
            var width = Mathf.Min(height * 2.75f, (safeArea.width - margin * 3f) / 2f);
            var bottom = safeArea.yMin + margin;
            // Recentre button sits between the two, if there is room (always in landscape).
            var aheadWidth = Mathf.Min(height * 1.6f, safeArea.width - (width + margin) * 2f - margin * 2f);
            var ahead = aheadWidth < height
                ? Rect.zero
                : new Rect(safeArea.center.x - aheadWidth / 2f, bottom, aheadWidth, height);
            return (new Rect(safeArea.xMin + margin, bottom, width, height),
                new Rect(safeArea.xMax - margin - width, bottom, width, height), ahead);
        }
    }
}
