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

        readonly Queue<Line> cardQueue = new();
        Line card, caption, pendingVoice, pendingAlert;
        float captionTimer, voiceTimer, alertTimer;
        bool chimeIfSilent;
        AudioSource voice;
        AudioClip chime;
        GUIStyle captionStyle, subtitleStyle, cardTitleStyle, cardTextStyle, hintStyle;

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
        internal bool ReadDismissInput()
        {
            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                var position = pointer.position.ReadValue();
                var cc = CcArea();
                if (cc.Contains(new Vector2(position.x, Screen.height - position.y)))
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

        // Plain text for the headset, where OnGUI is not drawn.
        internal string HeadsetText
        {
            get
            {
                if (card != null) return $"{card.Caption}\n{card.Text}\n(press a button to continue)";
                if (caption == null) return "";
                return Subtitles && caption.Text != caption.Caption ? $"{caption.Caption}\n{caption.Text}" : caption.Caption;
            }
        }

        internal void Draw(float buttonTop, float mirrorBottom)
        {
            EnsureStyles();
            var safe = Screen.safeArea;
            var guiSafe = new Rect(safe.x, Screen.height - safe.yMax, safe.width, safe.height);

            var cc = CcArea();
            var color = GUI.backgroundColor;
            GUI.backgroundColor = Subtitles ? new Color(.08f, .32f, .40f, .95f) : new Color(.07f, .12f, .16f, .85f);
            GUI.Box(cc, Subtitles ? "CC on" : "CC", hintStyle);
            GUI.backgroundColor = color;

            if (card != null)
            {
                // Just above the middle of the screen, but never over the rear-view mirror.
                var width = Mathf.Min(guiSafe.width * .8f, 960f);
                var body = card.Text != card.Caption ? card.Text : "";
                var titleHeight = cardTitleStyle.CalcHeight(new GUIContent(card.Caption), width - 48f);
                var textHeight = body.Length > 0 ? cardTextStyle.CalcHeight(new GUIContent(body), width - 48f) : 0f;
                var hintHeight = hintStyle.CalcHeight(new GUIContent("Tap anywhere to continue"), width - 48f);
                var height = titleHeight + textHeight + hintHeight + 56f;
                var rect = new Rect(guiSafe.center.x - width / 2f, Mathf.Max(guiSafe.y + 8f, mirrorBottom + 10f, Screen.height / 2f - height - 6f), width, height);
                GUI.backgroundColor = new Color(.05f, .09f, .12f, .96f);
                GUI.Box(rect, GUIContent.none);
                GUI.backgroundColor = color;
                var y = rect.y + 18f;
                GUI.Label(new Rect(rect.x + 24f, y, width - 48f, titleHeight), card.Caption, cardTitleStyle);
                y += titleHeight + 8f;
                if (body.Length > 0) GUI.Label(new Rect(rect.x + 24f, y, width - 48f, textHeight), body, cardTextStyle);
                y += textHeight + 12f;
                GUI.Label(new Rect(rect.x + 24f, y, width - 48f, hintHeight), "Tap anywhere to continue", hintStyle);
                return;
            }

            if (caption == null) return;
            // Low band above the buttons, so the road ahead stays clear.
            var capWidth = Mathf.Min(guiSafe.width * .7f, 1100f);
            var sub = Subtitles && caption.Text != caption.Caption ? caption.Text : "";
            var capHeight = captionStyle.CalcHeight(new GUIContent(caption.Caption), capWidth - 32f);
            var subHeight = sub.Length > 0 ? subtitleStyle.CalcHeight(new GUIContent(sub), capWidth - 32f) : 0f;
            var boxHeight = capHeight + subHeight + 20f;
            var box = new Rect(guiSafe.center.x - capWidth / 2f, buttonTop - boxHeight - 12f, capWidth, boxHeight);
            GUI.backgroundColor = caption.Kind == "fail" ? new Color(.45f, .08f, .06f, .9f) : new Color(.05f, .09f, .12f, .85f);
            GUI.Box(box, GUIContent.none);
            GUI.backgroundColor = color;
            GUI.Label(new Rect(box.x + 16f, box.y + 10f, capWidth - 32f, capHeight), caption.Caption, captionStyle);
            if (sub.Length > 0) GUI.Label(new Rect(box.x + 16f, box.y + 10f + capHeight, capWidth - 32f, subHeight), sub, subtitleStyle);
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

        static Rect CcArea()
        {
            var safe = Screen.safeArea;
            var size = Mathf.Clamp(Mathf.Min(safe.width, safe.height) * .09f, 44f, 110f);
            return new Rect(safe.xMax - size * 1.6f - 12f, Screen.height - safe.yMax + 12f, size * 1.6f, size);
        }

        void EnsureStyles()
        {
            var unit = Mathf.Min(Screen.width, Screen.height);
            if (captionStyle != null && captionStyle.fontSize == Mathf.RoundToInt(unit * .045f)) return;
            GUIStyle Make(float size, FontStyle style, TextAnchor anchor) => new(GUI.skin.label)
            {
                fontSize = Mathf.Max(14, Mathf.RoundToInt(unit * size)), fontStyle = style, alignment = anchor, wordWrap = true,
                normal = { textColor = Color.white }
            };
            captionStyle = Make(.045f, FontStyle.Bold, TextAnchor.MiddleCenter);
            subtitleStyle = Make(.03f, FontStyle.Normal, TextAnchor.MiddleCenter);
            cardTitleStyle = Make(.05f, FontStyle.Bold, TextAnchor.UpperLeft);
            cardTextStyle = Make(.035f, FontStyle.Normal, TextAnchor.UpperLeft);
            hintStyle = Make(.025f, FontStyle.Normal, TextAnchor.MiddleCenter);
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
