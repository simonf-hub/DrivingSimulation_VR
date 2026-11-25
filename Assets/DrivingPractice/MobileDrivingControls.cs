using UnityEngine;
using UnityEngine.InputSystem;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace BCRoot
{
    // Screen input for phones and the editor: taps on the two pedals (bottom right, side by side, so one thumb rocks
    // between them like a foot on real pedals), the Ahead button (top right), and swipes (or mouse drags) anywhere else
    // to look around.
    // The pedals are drawn by DrivingPractice.Pedals.cs using the same rectangles.
    internal sealed class MobileDrivingControls
    {
        Touchscreen lastScreen;
        int[] activeTouchIds;
        bool mouseLooking;

        internal static bool UsesTouch => Application.isMobilePlatform || Touchscreen.current != null;

        // The mouse works whenever no finger touched the screen in the last moment. Not gated on UsesTouch: browsers
        // on PCs can report a touchscreen (Unity web), which used to switch the mouse off. The delay ignores the
        // mouse clicks a phone browser fakes after a tap, so a pedal tap never counts twice.
        float lastTouchTime = float.NegativeInfinity;
        internal bool MouseAllowed => Time.unscaledTime - lastTouchTime > .8f;

        // Rectangles in screen pixels (origin bottom left).
        internal (bool LeftTap, bool RightTap, bool AheadTap, Vector2 LookDelta) Read()
        {
            var (slower, faster, ahead) = ButtonAreas(Screen.safeArea);
            bool left = false, right = false, aheadTap = false;
            var lookDelta = Vector2.zero;

            if (Touchscreen.current is { } touchscreen)
                foreach (var t in touchscreen.touches)
                    if (t.isInProgress) lastTouchTime = Time.unscaledTime;

            // Mouse (editor, desktop): a click on a pedal presses it; a drag that starts anywhere else looks around,
            // like a swipe on a phone.
            if (MouseAllowed && Mouse.current is { } mouse)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    var p = mouse.position.ReadValue();
                    left = slower.Contains(p);
                    right = faster.Contains(p);
                    aheadTap = ahead.Contains(p);
                    mouseLooking = !left && !right && !aheadTap && !CcArea(Screen.safeArea).Contains(p);
                }
                else if (mouse.leftButton.isPressed && mouseLooking) lookDelta = mouse.delta.ReadValue();
            }

            if (Touchscreen.current is not { } screen) return (left, right, aheadTap, lookDelta);
            if (screen != lastScreen)
            {
                lastScreen = screen;
                activeTouchIds = new int[screen.touches.Count];
            }

            var hasLookFinger = false;
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
                if (slower.Contains(start)) left |= isNew;
                else if (faster.Contains(start)) right |= isNew;
                else if (ahead.Contains(start)) aheadTap |= isNew;
                else if (!hasLookFinger && !isNew)
                {
                    lookDelta = touch.delta.ReadValue();
                    hasLookFinger = true;
                }
            }
            return (left, right, aheadTap, lookDelta);
        }

        // Brake (Slower) wide and flat, accelerator (Faster) tall and narrow, bottoms aligned, in the bottom-right corner.
        // The CC button: top right corner, right of Ahead.
        internal static Rect CcArea(Rect safeArea)
        {
            var pill = Mathf.Clamp(Mathf.Min(safeArea.width, safeArea.height) * .09f, 44f, 110f);
            return new Rect(safeArea.xMax - 12f - pill * 1.6f, safeArea.yMax - 12f - pill, pill * 1.6f, pill);
        }

        internal static (Rect Slower, Rect Faster, Rect Ahead) ButtonAreas(Rect safeArea)
        {
            var shortSide = Mathf.Min(safeArea.width, safeArea.height);
            var margin = Mathf.Max(14f, shortSide * .04f);
            var gasHeight = Mathf.Clamp(shortSide * .3f, 110f, 300f);
            var gas = new Rect(safeArea.xMax - margin - gasHeight * .56f, safeArea.yMin + margin, gasHeight * .56f, gasHeight);
            var brakeWidth = gasHeight * .9f;
            var brake = new Rect(gas.x - margin * .7f - brakeWidth, gas.y, brakeWidth, gasHeight * .62f);

            // Ahead: a small pill along the top right, left of the CC button.
            var pill = Mathf.Clamp(shortSide * .09f, 44f, 110f);
            var ahead = new Rect(safeArea.xMax - 12f - pill * 1.6f - 10f - pill * 2.1f, safeArea.yMax - 12f - pill, pill * 2.1f, pill);
            return (brake, gas, ahead);
        }
    }
}
