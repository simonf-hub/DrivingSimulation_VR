using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace BCRoot
{
    // Runs one authored scenario scene: the car on rails, input, look-around, the HUD, and pass/fail.
    // Each scenario's rules are in its own partial file (DrivingPractice.<Scenario>.cs); their numbers are in
    // Resources/Data/scenario_spec.json. ScenarioLauncher chains scenes into a session.
    public sealed partial class DrivingPractice : MonoBehaviour
    {
        enum Scenario { StopSign, HiddenCrossing, SchoolZone, Playground, Expressway, FlashingRed, Ambulance, SchoolBus }

        const float Lane = 1.85f;
        const float Nose = 2.66f; // front bumper, metres ahead of the player car's origin
        static readonly Vector3 SeatShift = new(0f, .02f, .10f); // car-local metres: eye now at about (-0.41, 1.21, 0.47)
        static int[] Speeds => PortSpec.Current.engine.gearsKmh;

        // scenario_spec.json ids, in Scenario enum order.
        static readonly string[] SpecIds = { "stop", "hidden_ped", "school", "playground", "lead_car", "flashing_red", "ambulance", "school_bus" };

        [SerializeField] Section authoredScenario;
        [SerializeField] Transform vehicle;
        [SerializeField] Transform driverView;
        [SerializeField] TextMesh display;

        readonly List<Material> materials = new();
        Transform layout; // the scene's "Scenario layout" content: road, ground, props
        Bounds playerCarBounds;
        Material red, darkRedLens;
        AudioSource speaker, voice;
        AudioClip prompt, siren;
        BeaverCoach coach = new();
        bool startedOnce;
        int gear;
        float changeRate;
        bool queuedSlower;
        // Set by scenarios while a hazard or emergency vehicle is active: a second Slower tap is queued, not a rear-end fail.
        internal bool EmergencyActive;
        float stoppedFor, roadOverTime, nudgeTimer;
        float carX = Lane, carXTarget = Lane;
        bool scenarioHold; // a scenario sets this while a hazard is still to come, to keep nudge_go quiet
        float speed, distance;
        bool leftHeld, rightHeld, finished;
        string result;
        bool completedSessionScenario;
        float worldStart;
        readonly List<Light> stagedLights = new();
        Vector3 seatedHead;
        Quaternion seatedHeading;
        float yaw, pitch, tiltReference;
        bool usingHeadset, usingTilt;
        MobileDrivingControls mobileControls = new();

        // Flashing red reuses the stop sign's numbers ("sameAs": "stop").
        static string SpecIdFor(Scenario kind) => kind == Scenario.FlashingRed ? "stop" : SpecIds[(int)kind];

        internal float Length => PortSpec.Current.Scenario(SpecIdFor(authoredScenario.Kind)).lengthMetres;
        internal float WorldEnd => worldStart + Length;

        // The scenario's objects, wired in the scene Inspector. Each scenario uses only the ones it needs.
        [Serializable]
        sealed class Section
        {
            public Scenario Kind;
            public Transform Actor;
            public Transform ParkedCar;
            public Transform Animal; // parked truck: the dog variant (Actor is the pedestrian)
            public Transform TrafficSignal;
            [NonSerialized] public Vector3 ActorStart;
            public GameObject Signal;
            [NonSerialized] public MeshRenderer RedLensRenderer;
            [NonSerialized] public Material[] RedLensOn;
            [NonSerialized] public Material[] RedLensOff;
            [NonSerialized] public bool RedLensLit;
            public GameObject BrakeLights;
            public GameObject CrossingChild;
            public GameObject StopArm;
            public GameObject Ball;
        }

        void Awake()
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
                if (root.name == "Scenario layout" && root.transform.childCount > 0) layout = root.transform.GetChild(0);
            if (!layout) Debug.LogError("No 'Scenario layout' object with the scenario's content in this scene.", this);

            red = Surface(new Color(1f, .025f, .01f), true);
            InitializeSection(authoredScenario);
            speaker = gameObject.AddComponent<AudioSource>();
            speaker.playOnAwake = false;
            prompt = Tone("Approach cue", .25f, false);
            siren = Tone("Emergency siren", 1.4f, true);
            voice = gameObject.AddComponent<AudioSource>(); // own source, so the beaver never cuts off the siren
            voice.playOnAwake = false;
            coach.Attach(voice, prompt);
            playerCarBounds = CarBoundsRelativeTo(vehicle);
            // Eye a little forward and up from the scene's seat: more road over the dashboard and less roof.
            // Before the mirrors, which aim at the eye. In a headset the tracked head moves relative to this seat.
            driverView.parent.position += vehicle.TransformDirection(SeatShift);
            CreateMirror();
            CreateSideMirrors();
            ApplyLook(); // DrivingPractice.Sky.cs; before the mountains read the fog colour
            CreateInteriorRoof();
            CreateHud();
            CreatePedals();
            CreateBeaver();
            CreateCards();
            StartRound();
        }

        void Start()
        {
            // The plain street past the end (Editor/StreetDresser.cs) gives way to the next scenario when one follows.
            ShowStreetPart("Run-out", !ScenarioLauncher.HasFollowingScenario(gameObject.scene.path));
            if (worldStart == 0f) ScenarioLauncher.PreloadFollowing(this);
        }

        void ShowStreetPart(string part, bool show)
        {
            var street = layout ? layout.Find("Streetscape/" + part) : null;
            if (street) street.gameObject.SetActive(show);
        }

        internal void StageForSession(float start)
        {
            worldStart = start;
            enabled = false;
            vehicle.gameObject.SetActive(false);
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (root == gameObject || root == vehicle.gameObject) continue;
                root.transform.position += Vector3.forward * start;
            }

            SetDirectionalLights(false);

            // The first scene has a lead-in behind its spawn point. Join later roads at zero.
            if (!layout) return;
            ShowStreetPart("Run-in", false); // the previous scenario's street is there
            foreach (Transform prop in layout)
            {
                var position = prop.localPosition;
                if (position.z < 0f && prop.name == "Centre marking")
                    prop.gameObject.SetActive(false);
                if (prop.name != "Road" && prop.name != "Ground") continue;
                var scale = prop.localScale;
                if (position.z - scale.z / 2f >= 0f) continue;
                scale.z = position.z + scale.z / 2f;
                position.z = scale.z / 2f;
                prop.localScale = scale;
                prop.localPosition = position;
            }
        }

        internal void LeaveSessionSection()
        {
            speaker.Stop();
            enabled = false;
            vehicle.gameObject.SetActive(false);
            SetDirectionalLights(false);
        }

        internal void EnterSessionSection(DrivingPractice previous)
        {
            ApplyLook(); // this scene is now the active one, so its lighting settings are the ones in use
            distance = previous.distance - previous.Length;
            vehicle.position = previous.vehicle.position;
            speed = previous.speed;
            gear = previous.gear;
            changeRate = previous.changeRate;
            mobileControls = previous.mobileControls;
            coach = previous.coach;
            coach.Attach(voice, prompt);
            leftHeld = previous.leftHeld;
            rightHeld = previous.rightHeld;
            yaw = previous.yaw;
            pitch = previous.pitch;
            usingTilt = previous.usingTilt;
            tiltReference = previous.tiltReference;
            seatedHead = previous.seatedHead;
            seatedHeading = previous.seatedHeading;
            usingHeadset = previous.usingHeadset;
            driverView.SetLocalPositionAndRotation(previous.driverView.localPosition, previous.driverView.localRotation);

            SetDirectionalLights(true);
            vehicle.gameObject.SetActive(true);
            enabled = true;
            ShowDrive();
        }

        void SetDirectionalLights(bool restore)
        {
            if (restore)
            {
                foreach (var light in stagedLights) light.enabled = true;
                stagedLights.Clear();
                return;
            }

            foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var light in root.GetComponentsInChildren<Light>())
            {
                if (light.type != LightType.Directional || !light.enabled) continue;
                light.enabled = false;
                stagedLights.Add(light);
            }
        }

        void InitializeSection(Section section)
        {
            if (section.Actor) section.ActorStart = section.Actor.localPosition;
            if (section.TrafficSignal) ConfigureFlashingLens(section, section.TrafficSignal);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var left = Pressed(XRNode.LeftHand) || keyboard?.leftArrowKey.isPressed == true;
            var right = Pressed(XRNode.RightHand) || keyboard?.rightArrowKey.isPressed == true;
            var touch = mobileControls.Read();
            var dismissTap = coach.ReadDismissInput(MobileDrivingControls.CcArea(Screen.safeArea));
            UpdateView(touch.LookDelta, touch.AheadTap || keyboard?.upArrowKey.wasPressedThisFrame == true);
            coach.TiltActive = usingTilt;
            UpdateMirrorAlert();
            UpdateSideMirrors();
            coach.Tick(Time.unscaledDeltaTime);
            UpdateHud(); // also while a card is up or the result shows, so the HUD is never blank
            var touchEnabled = !usingHeadset;
            var upshift = (right && !rightHeld) || (touchEnabled && touch.RightTap);
            var downshift = (left && !leftHeld) || (touchEnabled && touch.LeftTap);
            leftHeld = left;
            rightHeld = right;
            left |= touchEnabled && touch.LeftTap;
            right |= touchEnabled && touch.RightTap;
            UpdatePedals(downshift, upshift);

            if (result != null)
            {
                if (upshift && !left)
                {
                    if (!completedSessionScenario) StartRound();
                    else if (!ScenarioLauncher.HasRemaining)
                    {
                        enabled = false;
                        ScenarioLauncher.RestartSession();
                    }
                }
                else ShowResult();
                return;
            }

            // Pause and explain: the drive is frozen, and any tap (Faster/Slower included) only closes the card.
            if (coach.Paused)
            {
                if (dismissTap || upshift || downshift || touch.AheadTap) coach.Dismiss();
                ShowDrive();
                return;
            }

            if (downshift && !right && gear > 0)
            {
                if (StillSlowing)
                {
                    if (EmergencyActive) queuedSlower = true;
                    else
                    {
                        Fail("rear_ended", "rear_ended");
                        return;
                    }
                }
                else SetGear(gear - 1);
            }
            if (upshift && !left && gear < Speeds.Length - 1) SetGear(gear + 1);

            // Limit movement per step so a slow frame cannot jump over a stop line.
            var remaining = Mathf.Min(Time.deltaTime, .1f);
            while (remaining > 0f && result == null && enabled && !coach.Paused)
            {
                var step = Mathf.Min(remaining, .02f);
                Advance(step);
                coach.TickGame(step);
                remaining -= step;
            }
            if (!enabled) return;
            if (result != null) ShowResult();
            else ShowDrive();
        }

        void Advance(float dt)
        {
            var section = authoredScenario;
            var kind = section.Kind;
            speed = Mathf.MoveTowards(speed, Speeds[gear], changeRate * dt);
            if (queuedSlower && !StillSlowing)
            {
                queuedSlower = false;
                if (gear > 0) SetGear(gear - 1);
            }
            distance += speed / 3.6f * dt;
            var front = distance + Nose; // u: front bumper metres past the section start
            // Sideways steering toward carXTarget (the ambulance pull-over).
            carX += (carXTarget - carX) * Mathf.Min(1f, dt * PortSpec.Current.engine.pullOverLateralRate);
            vehicle.position = new Vector3(carX, 0f, worldStart + distance);
            stoppedFor = speed <= .01f ? stoppedFor + dt : 0f;

            // Road speed limit, every scenario: over the limit for longer than the grace time.
            var limitSpec = PortSpec.Current.engine.speedLimit;
            var limit = PortSpec.Current.Scenario(SpecIdFor(kind)).limitKmh;
            roadOverTime = speed > limit + limitSpec.toleranceKmh ? roadOverTime + dt : 0f;
            if (roadOverTime > limitSpec.graceSeconds) { Fail("speeding", "speeding"); return; }

            var running = kind switch
            {
                Scenario.StopSign or Scenario.FlashingRed => StepStopSign(section, front, dt),
                Scenario.HiddenCrossing => StepHiddenCrossing(front, dt),
                Scenario.SchoolZone => StepSchoolZone(front, dt),
                Scenario.Playground => StepPlayground(section, front, dt),
                Scenario.Expressway => StepLeadCar(section, front, dt),
                Scenario.Ambulance => StepAmbulance(section, front, dt),
                _ => StepSchoolBus(section, front, dt)
            };
            if (!running) return;
            NudgeWhenWaiting(dt);
            if (distance < Length || result != null) return;
            FinishSection(section);
        }

        void FinishSection(Section section)
        {
            ClearScenarioProps(section);
            if (ScenarioLauncher.HasFollowingScenario(gameObject.scene.path))
            {
                if (!ScenarioLauncher.TryAdvance(this))
                {
                    distance = Length;
                    vehicle.position = new Vector3(Lane, 0f, worldStart + distance);
                }
                return;
            }
            finished = true;
            End("You cleared this scenario.");
        }

        static void ClearScenarioProps(Section section)
        {
            if (section.Actor) section.Actor.gameObject.SetActive(false);
            if (section.ParkedCar) section.ParkedCar.gameObject.SetActive(false);
            if (section.Animal) section.Animal.gameObject.SetActive(false);
            if (section.CrossingChild) section.CrossingChild.SetActive(false);
            if (section.Ball) section.Ball.SetActive(false);
            if (section.Signal) section.Signal.SetActive(false);
            if (section.StopArm) section.StopArm.SetActive(false);
            if (section.BrakeLights) section.BrakeLights.SetActive(false);
        }

        void StartRound()
        {
            result = null;
            resultLine = null;
            finished = false;
            completedSessionScenario = false;
            distance = 0;
            vehicle.position = new Vector3(Lane, 0f, worldStart);
            speed = 50f;
            ResetSection();
            coach.Reset();
            coach.Say(startedOnce ? "start_retry" : "start_practice");
            startedOnce = true;
            ShowDrive();
        }

        void ResetSection()
        {
            SetGear(2);
            queuedSlower = EmergencyActive = false;
            var section = authoredScenario;
            if (section.Actor)
            {
                section.Actor.localPosition = section.ActorStart;
                section.Actor.gameObject.SetActive(true);
            }
            if (section.ParkedCar) section.ParkedCar.gameObject.SetActive(true);
            if (section.CrossingChild) section.CrossingChild.SetActive(false);
            if (section.Signal) section.Signal.SetActive(false);
            if (section.StopArm) section.StopArm.SetActive(false);
            if (section.BrakeLights) section.BrakeLights.SetActive(false);
            stoppedFor = roadOverTime = nudgeTimer = zoneOverTime = 0f;
            scenarioHold = MirrorAlert = false;
            carX = carXTarget = Lane;
            switch (section.Kind)
            {
                case Scenario.StopSign:
                case Scenario.FlashingRed: ResetStopSign(section); break;
                case Scenario.HiddenCrossing: ResetHiddenCrossing(section); break;
                case Scenario.SchoolZone: ResetSchoolZone(); break;
                case Scenario.Playground: ResetPlayground(section); break;
                case Scenario.Expressway: ResetLeadCar(section); break;
                case Scenario.Ambulance: ResetAmbulance(section); break;
                case Scenario.SchoolBus: ResetSchoolBus(section); break;
            }
            speaker.Stop();
            speaker.loop = false;
            speaker.volume = 1f;
        }

        // Every fail names a code (for the log and results) and the beaver line that explains it.
        void Fail(string code, string lineId, string side = null, int? metres = null)
        {
            resultLine = coach.Fail(code, lineId, side, metres);
            End($"{resultLine.Caption}\n{resultLine.Text}");
        }

        void UpdateView(Vector2 touchDelta, bool recentre)
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out var position) &&
                head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out var rotation) &&
                (!head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out var tracked) || tracked))
            {
                if (!usingHeadset)
                {
                    seatedHead = position;
                    var forward = rotation * Vector3.forward;
                    seatedHeading = Quaternion.Euler(0f, -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 0f);
                    usingHeadset = true;
                }
                driverView.localPosition = seatedHeading * (position - seatedHead);
                driverView.localRotation = seatedHeading * rotation;
                return;
            }

            usingHeadset = false;
            var lookSpec = PortSpec.Current.engine.look;
            var maxYaw = lookSpec.maxRadians * Mathf.Rad2Deg;
            // Prototype swipe: yaw (radians) += swipeGain * pixels / focal length (pixels).
            var camera = driverView.GetComponentInChildren<Camera>();
            if (!camera) camera = Camera.main;
            var focal = Screen.height * .5f / Mathf.Tan((camera ? camera.fieldOfView : 60f) * .5f * Mathf.Deg2Rad);
            var degreesPerPixel = lookSpec.swipeGain / focal * Mathf.Rad2Deg;
            var mouse = Mouse.current;
            var look = touchDelta * degreesPerPixel;
            if (!MobileDrivingControls.UsesTouch && mouse?.rightButton.isPressed == true)
                look = mouse.delta.ReadValue() * degreesPerPixel;

            if (TryTiltHeading(out var heading))
            {
                if (!usingTilt || recentre) tiltReference = heading;
                usingTilt = true;
                yaw = Mathf.DeltaAngle(tiltReference, heading);
            }
            else
            {
                usingTilt = false;
                yaw = recentre ? 0f : yaw + look.x;
            }
            if (recentre) pitch = 0f;
            yaw = Mathf.Clamp(yaw, -maxYaw, maxYaw);
            pitch = Mathf.Clamp(pitch - look.y, -40f, 40f);
            driverView.localPosition = Vector3.zero;
            driverView.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        // Phone tilt: compass heading (degrees, right = positive) of the direction the phone's back faces.
        // shortcut: assumes the sensor's world frame is z-up (Android/iOS raw); if turning right looks left on a device, flip the sign here.
        static bool TryTiltHeading(out float heading)
        {
            heading = 0f;
            if (!Application.isMobilePlatform || AttitudeSensor.current is not { } sensor) return false;
            if (!sensor.enabled) InputSystem.EnableDevice(sensor);
            var attitude = sensor.attitude.ReadValue();
            if (attitude.x == 0f && attitude.y == 0f && attitude.z == 0f) return false; // no reading yet: swipe instead
            var back = attitude * Vector3.back;
            heading = Mathf.Atan2(back.x, back.y) * Mathf.Rad2Deg;
            return true;
        }

        // Stopped a while with nothing coming and the beaver quiet: "When the way is clear, tap Faster".
        void NudgeWhenWaiting(float dt)
        {
            var nudge = PortSpec.Current.engine.nudgeGo;
            if (stoppedFor > nudge.stoppedSeconds && !scenarioHold && Time.unscaledTime - coach.LastSayTime > nudge.quietSeconds)
            {
                if (nudgeTimer == 0f || nudgeTimer > nudge.repeatSeconds)
                {
                    nudgeTimer = 0f;
                    coach.Say("nudge_go");
                }
                nudgeTimer += dt;
            }
            else nudgeTimer = 0f;
        }

        bool StillSlowing => speed > Speeds[gear] + PortSpec.Current.engine.rearEnd.toleranceKmh;

        // One gear step takes a fixed time whatever its size: slowerStepSeconds down, fasterStepSeconds up.
        void SetGear(int value)
        {
            var engine = PortSpec.Current.engine;
            gear = value;
            var target = Speeds[gear];
            var seconds = target < speed ? engine.slowerStepSeconds : engine.fasterStepSeconds;
            if (target < speed) slowFromSpeed = speed; // for the brake's "still slowing" fill
            changeRate = Mathf.Max(engine.minChangeRateKmhPerSecond, Mathf.Abs(speed - target) / seconds);
        }

        static bool Pressed(XRNode node) => InputDevices.GetDeviceAtXRNode(node)
            .TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out var pressed) && pressed;

        void End(string message)
        {
            result = message;
            speaker.Stop();
            speed = 0f;

            completedSessionScenario = finished && ScenarioLauncher.TryCompleteFinalScenario(gameObject.scene.path);
        }

        // On a phone the result card shows this (DrivingPractice.Cards.cs); the dashboard text is the headset fallback.
        void ShowResult()
        {
            if (!usingHeadset)
            {
                display.text = "";
                return;
            }
            if (completedSessionScenario)
            {
                var progress = $"{ScenarioLauncher.CompletedCount}/{ScenarioLauncher.ScenarioCount}";
                var restart = MobileDrivingControls.UsesTouch && !usingHeadset
                    ? "Tap New session" : "Right button / → for a new session";
                display.text = $"SESSION COMPLETE   {progress}\n{restart}";
                return;
            }

            var retryPrompt = MobileDrivingControls.UsesTouch && !usingHeadset
                ? "Tap Retry" : "Right button / → to retry this scenario";
            display.text = $"{(finished ? "ROUND COMPLETE" : "TRY AGAIN")}\n{result}\n{retryPrompt}";
        }

        // While driving, speed and limit are on the windshield HUD; the dashboard text only carries the beaver's words
        // in a headset, where the screen overlay is not drawn.
        void ShowDrive() => display.text = usingHeadset ? coach.HeadsetText : "";

        void OnDestroy()
        {
            DestroyMirror();
            DestroySideMirrors();
            DestroyPedals();
            DestroyBeaver();
            if (headlinerMesh) Destroy(headlinerMesh);
            foreach (var material in materials) Destroy(material);
            if (prompt) Destroy(prompt);
            if (siren) Destroy(siren);
        }

        static Bounds CarBoundsRelativeTo(Transform car)
        {
            var bounds = new Bounds(car.position, Vector3.zero);
            foreach (var mesh in car.GetComponentsInChildren<MeshFilter>())
                if (mesh.TryGetComponent<MeshRenderer>(out var renderer) && renderer.enabled)
                    bounds.Encapsulate(renderer.bounds);

            bounds.center -= car.position;
            return bounds;
        }

        void ConfigureFlashingLens(Section section, Transform signal)
        {
            foreach (var renderer in signal.GetComponentsInChildren<MeshRenderer>())
            {
                var slots = renderer.sharedMaterials;
                var lens = Array.FindIndex(slots, material => material && material.name == "TrafficLight_VernelhoAceso");
                if (lens < 0) continue;
                section.RedLensRenderer = renderer;
                section.RedLensOff = slots;
                section.RedLensOn = (Material[])slots.Clone();
                section.RedLensOn[lens] = red;

                if (!darkRedLens)
                {
                    darkRedLens = new Material(slots[lens]) { color = new Color(.22f, .02f, .02f) };
                    materials.Add(darkRedLens);
                }
                slots[lens] = darkRedLens;
                renderer.sharedMaterials = slots;
                return;
            }
            Debug.LogWarning("The traffic light prefab has no red lens material slot.", signal);
        }

        Material Surface(Color color, bool emissive = false)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            material.SetFloat("_Smoothness", .16f);
            if (emissive)
            {
                material.SetColor("_EmissionColor", color * .45f);
                material.EnableKeyword("_EMISSION");
            }
            materials.Add(material);
            return material;
        }

        static AudioClip Tone(string name, float duration, bool sweep)
        {
            const int rate = 22050;
            var samples = new float[Mathf.RoundToInt(rate * duration)];
            var phase = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var time = (float)i / rate;
                var frequency = sweep ? Mathf.Lerp(610f, 990f, 1f - Mathf.Abs(2f * time / duration - 1f)) : 830f;
                phase += 2f * Mathf.PI * frequency / rate;
                var envelope = Mathf.Clamp01(Mathf.Min(time, duration - time) * 90f);
                samples[i] = Mathf.Sin(phase) * envelope * .32f;
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
