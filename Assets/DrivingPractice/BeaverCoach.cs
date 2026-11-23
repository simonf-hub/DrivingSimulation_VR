using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BCRoot
{
    // The beaver's lines, ported from the prototype's say()/fail() (engine_mid.js, engine_tail.js).
    // Lines: Resources/Data/beaver_lines.json. Voice: Resources/Voice/<id>.wav. Timings: scenario_spec.json.
    internal sealed class BeaverCoach
    {
        internal sealed class Line
        {
            public string Id, Kind, Caption, Text, Side;
        }

        [Serializable] sealed class LineFile { public LineRow[] lines; }
        [Serializable] sealed class LineRow { public string id, kind, caption, text; }

        static Dictionary<string, LineRow> rows;

        // Practice coaches and pauses; the final test says only fail and info lines.
        internal bool Coaching = true;
        internal bool Subtitles;
        internal bool TiltActive;
        internal string LastFailCode { get; private set; }
        internal float LastSayTime { get; private set; } = float.NegativeInfinity;
        internal bool Paused => card != null;
        // What to draw (DrivingPractice.Cards.cs): the pause-and-explain card, else the short caption near the road.
        internal Line Card => card;
        internal Line Caption => caption;

        readonly Queue<Line> cardQueue = new();
        Line card, caption, pendingVoice, pendingAlert;
        float captionTimer, voiceTimer, alertTimer;
        bool chimeIfSilent;
        AudioSource voice;
        AudioClip chime;

        static PortSpec.EngineSpec Engine => PortSpec.Current.engine;
        static PortSpec.PauseSpec Pause => PortSpec.Current.modes.practice.pauseAndExplain;

        // Voice plays on the current scene's source; chained scenes re-attach theirs.
        internal void Attach(AudioSource source, AudioClip chimeClip)
        {
            voice = source;
            chime = chimeClip;
        }

        internal void Say(string id, string side = null, int? metres = null)
        {
            var line = Resolve(id, side, metres);
            if (line == null) return;
            if (line.Caption == "Look left, right, left" && !TiltActive) line.Caption = "Swipe: left, right, left";
            if (!Coaching && line.Kind != "fail" && line.Kind != "info" && id != "final_cleared") return;
            LastSayTime = Time.unscaledTime;

            if (Coaching && Array.IndexOf(Pause.pauseKinds, line.Kind) >= 0 && Array.IndexOf(Pause.noPauseLines, id) < 0)
            {
                // An alert chimes now, then pauses a moment later so the hazard is in view.
                if (line.Kind == "alert" && card == null)
                {
                    pendingAlert = line;
                    alertTimer = Pause.alertDelaySeconds;
                    PlayChime();
                    return;
                }
                cardQueue.Enqueue(line);
                if (card == null) ShowNextCard();
                return;
            }

            ShowCaption(line);
            SpeakSoon(line, true);
        }

        // Ends the drive's coaching and says the fail line. Returns it for the try-again screen.
        internal Line Fail(string code, string id, string side = null, int? metres = null)
        {
            ClearCards();
            LastFailCode = code;
            var line = Resolve(id, side, metres) ?? new Line { Id = id, Kind = "fail", Caption = code, Text = code };
            line.Kind = "fail";
            LastSayTime = Time.unscaledTime;
            ShowCaption(line);
            SpeakSoon(line, true);
            return line;
        }

        internal void Dismiss()
        {
            if (card == null) return;
            var closed = card;
            card = null;
            if (cardQueue.Count > 0) ShowNextCard();
            else ShowCaption(closed); // the short version stays near the road as a reminder
        }

        internal void Reset()
        {
            ClearCards();
            caption = pendingVoice = null;
            LastFailCode = null;
            if (voice) voice.Stop();
        }

        // Real time: voice delay and caption timers run even while a card freezes the drive.
        internal void Tick(float dt)
        {
            if (pendingVoice != null && (voiceTimer -= dt) <= 0f)
            {
                var line = pendingVoice;
                pendingVoice = null;
                if (!PlayVoice(line) && chimeIfSilent) PlayChime();
            }
            if (caption != null && (captionTimer -= dt) <= 0f) caption = null;
        }

        // Game time: called from the driving substeps, so it stops while paused.
        internal void TickGame(float dt)
        {
            if (pendingAlert == null || card != null || (alertTimer -= dt) > 0f) return;
            cardQueue.Enqueue(pendingAlert);
            pendingAlert = null;
            ShowNextCard();
        }

        // CC toggles on its button or the C key. Returns true when the player tapped to close a card.
        // ccArea is in screen pixels (origin bottom left).
        internal bool ReadDismissInput(Rect ccArea)
        {
            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                if (ccArea.Contains(pointer.position.ReadValue()))
                {
                    Subtitles = !Subtitles;
                    return false;
                }
                return Paused;
            }
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            if (keyboard.cKey.wasPressedThisFrame) Subtitles = !Subtitles;
            return Paused && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame ||
                keyboard.numpadEnterKey.wasPressedThisFrame);
        }

        // Plain text for the headset, where the screen overlay is not drawn.
        internal string HeadsetText
        {
            get
            {
                if (card != null) return $"{card.Caption}\n{card.Text}\n(press a button to continue)";
                if (caption == null) return "";
                return Subtitles && caption.Text != caption.Caption ? $"{caption.Caption}\n{caption.Text}" : caption.Caption;
            }
        }

        void ShowNextCard()
        {
            card = cardQueue.Dequeue();
            caption = null; // only the card on screen
            SpeakSoon(card, card.Kind != "alert"); // the alert already chimed
        }

        void ShowCaption(Line line)
        {
            caption = line;
            captionTimer = line.Kind == "fail" ? Engine.failCaptionSeconds : Engine.captionSeconds;
        }

        void SpeakSoon(Line line, bool chimeWhenSilent)
        {
            pendingVoice = line;
            voiceTimer = Engine.voiceDelaySeconds; // the text appears first; the voice follows
            chimeIfSilent = chimeWhenSilent;
        }

        bool PlayVoice(Line line)
        {
            if (!voice) return false;
            // {side} lines try <id>_left / <id>_right first.
            var clip = line.Side != null ? Resources.Load<AudioClip>($"Voice/{line.Id}_{line.Side}") : null;
            if (!clip) clip = Resources.Load<AudioClip>($"Voice/{line.Id}");
            if (!clip) return false;
            voice.Stop();
            voice.clip = clip;
            voice.Play();
            return true;
        }

        void PlayChime()
        {
            if (voice && chime) voice.PlayOneShot(chime, .35f);
        }

        void ClearCards()
        {
            card = pendingAlert = null;
            cardQueue.Clear();
        }

        static Line Resolve(string id, string side, int? metres)
        {
            if (rows == null) Load();
            if (!rows.TryGetValue(id, out var row))
            {
                Debug.LogError($"Unknown beaver line '{id}'.");
                return null;
            }
            string Fill(string s) => s.Replace("{side}", side ?? "{side}").Replace("{m}", metres?.ToString() ?? "{m}");
            return new Line { Id = id, Kind = row.kind, Caption = Fill(row.caption), Text = Fill(row.text), Side = side };
        }

        static void Load()
        {
            var asset = Resources.Load<TextAsset>("Data/beaver_lines");
            if (!asset) throw new InvalidOperationException("Missing Resources/Data/beaver_lines.json");
            rows = new Dictionary<string, LineRow>();
            foreach (var row in JsonUtility.FromJson<LineFile>(asset.text).lines) rows[row.id] = row;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => rows = null; // re-read edits on each Play, even with domain reload off
    }
}
