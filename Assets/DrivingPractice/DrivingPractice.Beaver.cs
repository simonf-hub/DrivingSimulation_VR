using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;

namespace BCRoot
{
    // The beaver rides in the passenger seat (as promised to the client). From the driver's seat it is about 99 degrees
    // to the right, past the 80-degree look limit, so a small camera films its face for a live portrait on the cards
    // and captions (DrivingPractice.Cards.cs). Each time it starts a line it plays the artist's nod clip.
    // shortcut: one clip only (nod), no blink or mouth; add them when the artist sends more clips (see Art/PLACEHOLDER_ART.md).
    public sealed partial class DrivingPractice
    {
        // Sitting on the passenger seat (player sedan model, car-local metres: cushion top y .70, backrest front z .36),
        // turned a little toward the driver. The model (about .94 m tall, pivot .058 below its seat) faces +Z, like the car.
        const float BeaverScale = .6f, BeaverTurn = -18f;
        static readonly Vector3 BeaverSeat = new(.36f, .70f - .058f * BeaverScale, .5f);
        static readonly Vector3 BeaverHead = new(0f, .78f, .03f); // model space: middle of the head, cap included
        const int PortraitSize = 256;

        Transform beaver;
        Vector3 beaverBaseScale;
        Camera portraitCamera;
        RenderTexture portraitTexture;
        PlayableGraph nodGraph;
        AnimationClipPlayable nod;
        float nodLength;
        bool wasSpeaking;

        internal Texture BeaverPortrait => portraitTexture;

        void CreateBeaver()
        {
            var model = Resources.Load<GameObject>("Beaver/Beaver_Nodding");
            AnimationClip clip = null;
            foreach (var c in Resources.LoadAll<AnimationClip>("Beaver/Beaver_Nodding"))
                if (!c.name.StartsWith("__preview")) clip = c; // the editor also lists a preview copy
            if (!model)
            {
                Debug.LogWarning("Beaver model missing from Resources/Beaver.", this);
                return;
            }
            beaver = Instantiate(model, vehicle).transform;
            beaver.name = "Beaver";
            beaverBaseScale = model.transform.localScale * BeaverScale;
            beaver.localScale = beaverBaseScale;
            beaver.localRotation = Quaternion.Euler(0f, BeaverTurn, 0f);
            beaver.localPosition = BeaverSeat;

            // The nod plays from code (no Animator Controller asset): paused on its first frame (the rest pose) until a line starts.
            if (clip)
            {
                var animator = beaver.GetComponent<Animator>();
                if (!animator) animator = beaver.gameObject.AddComponent<Animator>();
                nodGraph = PlayableGraph.Create("Beaver nod");
                nodGraph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime); // nods while a card pauses the drive
                nod = AnimationClipPlayable.Create(nodGraph, clip);
                AnimationPlayableOutput.Create(nodGraph, "Nod", animator).SetSourcePlayable(nod);
                nod.Pause();
                nodLength = clip.length;
                nodGraph.Play();
            }

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

            // Nod once as each line starts; back to the rest pose when the clip ends.
            var speaking = coach.Speaking;
            if (nodGraph.IsValid())
            {
                if (speaking && !wasSpeaking)
                {
                    nod.SetTime(0d);
                    nod.Play();
                }
                else if (nod.GetPlayState() == PlayState.Playing && nod.GetTime() >= nodLength)
                {
                    nod.SetTime(0d);
                    nod.Pause();
                }
            }
            wasSpeaking = speaking;

            var breathe = 1f + Mathf.Sin(Time.unscaledTime * 1.6f) * .008f;
            beaver.localScale = Vector3.Scale(beaverBaseScale, new Vector3(1f, breathe, 1f));
        }

        void DestroyBeaver()
        {
            if (nodGraph.IsValid()) nodGraph.Destroy();
            if (portraitCamera) portraitCamera.targetTexture = null;
            if (!portraitTexture) return;
            portraitTexture.Release();
            Destroy(portraitTexture);
        }
    }
}
