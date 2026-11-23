using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BCRoot
{
    // The beaver rides in the passenger seat (as promised to the client). From the driver's seat it is about 99 degrees
    // to the right, past the 80-degree look limit, so a small camera films its face for a live portrait on the cards
    // and captions (DrivingPractice.Cards.cs). While its voice plays it bobs and tilts its head; it blinks now and then.
    // shortcut: the model is static (no rig or blendshapes), so "expressions" are whole-body motion and squashed eyes;
    // replace with real expressions when the artist adds a rig or face shapes (see Art/PLACEHOLDER_ART.md).
    public sealed partial class DrivingPractice
    {
        // On the passenger seat (player sedan model, car-local metres: cushion top y .70, backrest front z .36),
        // turned a little toward the driver. The model's pivot is its centre and it is about 1 m tall, so y = .70 + half its height.
        const float BeaverScale = .55f, BeaverTurn = -18f;
        static readonly Vector3 BeaverSeat = new(.36f, .70f + .5f * BeaverScale, .57f);
        static readonly Vector3 BeaverHead = new(0f, .27f, .1f); // model space: middle of the head, cap included
        const int PortraitSize = 256;

        Transform beaver, beaverEyeLeft, beaverEyeRight;
        Vector3 eyeScaleLeft, eyeScaleRight, beaverBaseScale;
        Camera portraitCamera;
        RenderTexture portraitTexture;
        float beaverTalkClock, blinkIn = 3f, blinkFor;

        internal Texture BeaverPortrait => portraitTexture;

        void CreateBeaver()
        {
            var model = Resources.Load<GameObject>("Beaver/Beaver_Nov23");
            if (!model)
            {
                Debug.LogWarning("Beaver model missing from Resources/Beaver.", this);
                return;
            }
            beaver = Instantiate(model, vehicle).transform;
            beaver.name = "Beaver";
            beaverBaseScale = model.transform.localScale * BeaverScale;
            beaver.localScale = beaverBaseScale;
            beaver.localRotation = Quaternion.Euler(0f, BeaverTurn, 0f); // the model faces +Z, like the car
            beaver.localPosition = BeaverSeat;
            beaverEyeLeft = beaver.Find("polySurface32");
            beaverEyeRight = beaver.Find("polySurface31");
            if (beaverEyeLeft) eyeScaleLeft = beaverEyeLeft.localScale;
            if (beaverEyeRight) eyeScaleRight = beaverEyeRight.localScale;

            // Portrait camera: in front of the beaver's face, looking back at it.
            portraitTexture = new RenderTexture(PortraitSize, PortraitSize, 16) { name = "Beaver portrait" };
            var turn = Quaternion.Euler(0f, BeaverTurn, 0f);
            var head = BeaverSeat + turn * (BeaverHead * BeaverScale);
            var holder = new GameObject("Beaver portrait camera").transform;
            holder.SetParent(vehicle, false);
            holder.localPosition = head + turn * Vector3.forward * .45f + Vector3.up * .03f; // stays behind the windshield
            holder.localRotation = Quaternion.LookRotation(head - holder.localPosition);
            portraitCamera = holder.gameObject.AddComponent<Camera>();
            portraitCamera.targetTexture = portraitTexture;
            portraitCamera.fieldOfView = 50f;
            portraitCamera.nearClipPlane = .05f;
            portraitCamera.farClipPlane = 4f;
            portraitCamera.depth = -11f;
            portraitCamera.enabled = false; // only renders while a portrait is on screen
            portraitCamera.GetUniversalAdditionalCameraData().renderShadows = false;
        }

        // Called every frame; showPortrait is true while a card or caption with the portrait is on screen.
        void UpdateBeaver(bool showPortrait)
        {
            if (!beaver) return;
            portraitCamera.enabled = showPortrait && !usingHeadset;

            var dt = Time.unscaledDeltaTime;
            var talking = coach.Speaking;
            beaverTalkClock = talking ? beaverTalkClock + dt : 0f;
            var bob = talking ? Mathf.Abs(Mathf.Sin(beaverTalkClock * 9f)) * .012f : 0f;
            var tilt = talking ? Mathf.Sin(beaverTalkClock * 3.1f) * 4f : 0f;
            var breathe = 1f + Mathf.Sin(Time.unscaledTime * 1.6f) * .008f;
            beaver.localPosition = BeaverSeat + Vector3.up * bob;
            beaver.localRotation = Quaternion.Euler(0f, BeaverTurn, tilt);
            beaver.localScale = Vector3.Scale(beaverBaseScale, new Vector3(1f, breathe, 1f));

            // Blink: squash the eyes for a moment every few seconds.
            blinkIn -= dt;
            if (blinkIn <= 0f)
            {
                blinkFor = .12f;
                blinkIn = Random.Range(2.5f, 5f);
            }
            blinkFor = Mathf.Max(0f, blinkFor - dt);
            var squash = blinkFor > 0f ? .12f : 1f;
            if (beaverEyeLeft) beaverEyeLeft.localScale = new Vector3(eyeScaleLeft.x, eyeScaleLeft.y * squash, eyeScaleLeft.z);
            if (beaverEyeRight) beaverEyeRight.localScale = new Vector3(eyeScaleRight.x, eyeScaleRight.y * squash, eyeScaleRight.z);
        }

        void DestroyBeaver()
        {
            if (portraitCamera) portraitCamera.targetTexture = null;
            if (!portraitTexture) return;
            portraitTexture.Release();
            Destroy(portraitTexture);
        }
    }
}
