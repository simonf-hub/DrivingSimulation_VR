using System;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace BCRoot
{
    public sealed class DrivingPractice : MonoBehaviour
    {
        enum Scenario { StopSign, HiddenCrossing, SchoolZone, Playground, Expressway, FlashingRed, Ambulance, SchoolBus }

        const float SectionLength = 160f;
        const float Conflict = 87f;
        const float EmergencyOffset = 30f;
        const float EmergencyStopLine = 84f + EmergencyOffset;
        const float CrossTrafficDuration = 6f;
        const float Lane = 1.85f;
        const float Nose = 2.66f;
        const int RoundLength = 5;
        static readonly int[] Speeds = { 0, 30, 50, 80 };
        static readonly string[] Names = {
            "Stop sign", "Obstructed crossing", "School zone", "Playground zone",
            "Expressway", "Flashing red", "Emergency vehicle", "School bus"
        };

        [SerializeField] bool useAuthoredScenario;
        [SerializeField] Section authoredScenario;
        [SerializeField] Transform vehicle;
        [SerializeField] Transform driverView;
        [SerializeField] TextMesh display;
        [SerializeField] GameObject city;
        [SerializeField] GameObject sedan, stopSign, schoolZone, playground, limit30, limit50, limit80;
        [SerializeField] GameObject trafficLight, ambulance, schoolBus, pedestrian, cat;

        readonly List<Section> course = new();
        readonly List<MeshRenderer> cityBuildings = new();
        MeshRenderer schoolBuilding;
        readonly List<Material> materials = new();
        GameObject route;
        Bounds playerCarBounds;
        Material asphalt, paint, grass, sidewalk, leaves, bark, red, amber, darkRedLens;
        AudioSource speaker;
        AudioClip prompt, siren;
        int sectionIndex, gear;
        float speed, distance, hazardTime, overspeedTime, closeTime, stoppedTime, leadPosition, leadSpeed;
        bool prompted, checkedLeft, checkedRight, stopped, leftHeld, rightHeld, finished;
        string result;
        bool completedSessionScenario;
        Vector3 seatedHead;
        Quaternion seatedHeading;
        float yaw, pitch;
        bool usingHeadset;

        [Serializable]
        sealed class Section
        {
            public Scenario Kind;
            public Transform Actor;
            public Transform ParkedCar;
            public Transform TrafficSignal;
            public Transform StopLine;
            [NonSerialized] public Bounds ParkedCarBounds;
            [NonSerialized] public Vector3 ActorStart, ChildStart, BallStart;
            public GameObject Signal;
            [NonSerialized] public MeshRenderer RedLensRenderer;
            [NonSerialized] public Material[] RedLensOn;
            [NonSerialized] public Material[] RedLensOff;
            [NonSerialized] public bool RedLensLit;
            public GameObject BrakeLights;
            public GameObject CrossingChild;
            public GameObject StopArm;
            public GameObject Ball;
            public GameObject Cue;
        }

        void Awake()
        {
            if (!useAuthoredScenario)
            {
                CreateSurfaces();
                IndexCityBuildings();
            }
            else
            {
                red = Surface(new Color(1f, .025f, .01f), true);
                InitializeSection(authoredScenario);
            }
            speaker = gameObject.AddComponent<AudioSource>();
            speaker.playOnAwake = false;
            prompt = Tone("Approach cue", .25f, false);
            siren = Tone("Emergency siren", 1.4f, true);
            playerCarBounds = CarBoundsRelativeTo(vehicle);
            StartRound();
        }

        void CreateSurfaces()
        {
            asphalt = Surface(new Color(.12f, .15f, .17f));
            paint = Surface(new Color(.94f, .93f, .85f));
            grass = Surface(new Color(.26f, .43f, .26f));
            sidewalk = Surface(new Color(.54f, .55f, .52f));
            leaves = Surface(new Color(.18f, .39f, .27f));
            bark = Surface(new Color(.27f, .19f, .12f));
            red = Surface(new Color(1f, .025f, .01f), true);
            amber = Surface(new Color(1f, .68f, .09f), true);
        }

        void IndexCityBuildings()
        {
            foreach (var renderer in city.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.name.StartsWith("building-", StringComparison.Ordinal) ||
                    renderer.name.StartsWith("building-skyscraper-", StringComparison.Ordinal)) continue;

                cityBuildings.Add(renderer);
                if (renderer.name == "building-e") schoolBuilding = renderer;
            }
        }

        void InitializeSection(Section section)
        {
            if (section.Actor) section.ActorStart = section.Actor.localPosition;
            if (section.CrossingChild) section.ChildStart = section.CrossingChild.transform.localPosition;
            if (section.Ball) section.BallStart = section.Ball.transform.localPosition;
            if (section.ParkedCar) section.ParkedCarBounds = CarBoundsRelativeTo(section.ParkedCar);
            if (section.TrafficSignal) ConfigureFlashingLens(section, section.TrafficSignal);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var left = Pressed(XRNode.LeftHand) || keyboard?.leftArrowKey.isPressed == true;
            var right = Pressed(XRNode.RightHand) || keyboard?.rightArrowKey.isPressed == true;
            var upshift = right && !rightHeld;
            var downshift = left && !leftHeld;
            leftHeld = left;
            rightHeld = right;
            UpdateView();

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

            if (downshift && !right && gear > 0)
            {
                if (speed > Speeds[gear] + .15f)
                {
                    End("Braked again before the previous downshift settled.");
                    return;
                }
                gear--;
            }
            if (upshift && !left) gear = Mathf.Min(gear + 1, Speeds.Length - 1);

            // Limit movement per step so a slow frame cannot jump over a stop line.
            var remaining = Mathf.Min(Time.deltaTime, .1f);
            while (remaining > 0f && result == null)
            {
                var step = Mathf.Min(remaining, .02f);
                Advance(step);
                remaining -= step;
            }
            if (result != null) ShowResult();
            else ShowDrive();
        }

        void Advance(float dt)
        {
            var section = course[sectionIndex];
            var kind = section.Kind;
            var local = distance - sectionIndex * SectionLength;
            if (!prompted && local >= 43f + (kind == Scenario.Ambulance ? EmergencyOffset : 0f))
            {
                prompted = true;
                section.Cue.SetActive(true);
                if (kind == Scenario.Ambulance)
                {
                    section.Actor.gameObject.SetActive(true);
                    speaker.clip = siren;
                    speaker.loop = true;
                    speaker.Play();
                }
                else speaker.PlayOneShot(prompt, .35f);
            }

            var previousPosition = vehicle.position;
            speed = Mathf.MoveTowards(speed, Speeds[gear], 20f * dt);
            distance += speed / 3.6f * dt;
            local = distance - sectionIndex * SectionLength;
            var front = local + Nose;
            var emergency = kind == Scenario.Ambulance;
            var emergencyLocal = local - EmergencyOffset;
            var pullOver = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(54f, 74f, emergencyLocal)) *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(90f, 115f, emergencyLocal)));
            vehicle.position = new Vector3(Lane + (emergency ? pullOver * 2.55f : 0f), 0f, distance);

            if (section.ParkedCar && HitParkedCar(section, previousPosition))
            {
                End("Hit the parked car.");
                return;
            }

            var limitZone = kind == Scenario.SchoolZone || kind == Scenario.Playground;
            if (limitZone && front >= 75f && local < 128f)
            {
                overspeedTime = speed > 30.1f ? overspeedTime + dt : 0f;
                if (overspeedTime > 1f) { End("Exceeded 30 km/h inside the zone."); return; }
            }

            if (kind == Scenario.Expressway)
            {
                leadSpeed = Mathf.MoveTowards(leadSpeed, front >= 77f ? 50f : 80f, 22f * dt);
                leadPosition += leadSpeed / 3.6f * dt;
                section.Actor.localPosition = new Vector3(section.ActorStart.x, section.ActorStart.y, leadPosition);
                section.BrakeLights.SetActive(front >= 77f && leadSpeed > 50.05f);
                var gap = leadPosition - 2f - front;
                closeTime = gap < speed / 3.6f * 1.25f ? closeTime + dt : 0f;
                if (gap <= 0f) { End("Rear-ended the lead vehicle."); return; }
                if (closeTime > 1.5f) { End("Followed the lead vehicle too closely."); return; }
            }
            else if (kind != Scenario.SchoolZone)
            {
                HandleStop(section, front, dt);
            }

            if (local < SectionLength || result != null) return;
            sectionIndex++;
            if (sectionIndex == course.Count)
            {
                finished = true;
                End(useAuthoredScenario ? "You cleared this scenario." : "You cleared all five scenarios.");
                return;
            }
            ResetSection();
        }

        void HandleStop(Section section, float front, float dt)
        {
            var kind = section.Kind;
            var intersection = kind == Scenario.StopSign || kind == Scenario.FlashingRed;
            var emergency = kind == Scenario.Ambulance;
            var bus = kind == Scenario.SchoolBus;
            var stopLine = StopLinePosition(section);
            var trigger = bus ? 45f : emergency ? 62f + EmergencyOffset : intersection ? 67f : 70f;
            if (front >= trigger) hazardTime += dt;
            var clearAfter = intersection ? CrossTrafficDuration : bus ? 8f : emergency ? 6.7f : 4.8f;
            var clear = hazardTime >= clearAfter;

            var flashOn = Mathf.Repeat(Time.time, .85f) < .43f;
            if (section.RedLensRenderer && section.RedLensLit != flashOn)
            {
                section.RedLensLit = flashOn;
                section.RedLensRenderer.sharedMaterials = flashOn ? section.RedLensOn : section.RedLensOff;
            }
            if (section.Signal)
                section.Signal.SetActive(!clear && hazardTime >= 1.4f && flashOn);
            var actor = section.Actor;
            if (intersection)
            {
                actor.localPosition = section.ActorStart + Vector3.right * Mathf.Lerp(0f, 36f, Mathf.InverseLerp(1.5f, clearAfter, hazardTime));
            }
            else if (emergency)
            {
                actor.localPosition = section.ActorStart + Vector3.forward * Mathf.Lerp(0f, 110f, Mathf.Clamp01(hazardTime / clearAfter));
                speaker.volume = clear ? 0f : Mathf.Lerp(.08f, .4f, Mathf.Clamp01(hazardTime / 4f));
            }
            else if (bus)
            {
                section.StopArm.SetActive(hazardTime >= 1.4f && !clear);
                actor.localPosition = section.ActorStart + Vector3.back * Mathf.Lerp(0f, 36f, Mathf.Clamp01(hazardTime / 1.4f));
                section.CrossingChild.transform.localPosition = section.ChildStart + Vector3.right *
                    Mathf.Lerp(0f, 11f, Mathf.InverseLerp(2.7f, clearAfter, hazardTime));
                section.CrossingChild.SetActive(hazardTime >= 2.7f);
            }
            else
            {
                actor.localPosition = section.ActorStart + Vector3.left * Mathf.Lerp(0f, 10.7f, Mathf.InverseLerp(.6f, clearAfter, hazardTime));
                if (section.Ball) section.Ball.transform.localPosition =
                    section.BallStart + Vector3.left * Mathf.Lerp(0f, 10.8f, Mathf.Clamp01(hazardTime / 1.7f));
            }

            if (front >= stopLine - 24f && front <= stopLine && speed <= .1f && (!emergency || vehicle.position.x > 4f))
            {
                stoppedTime += dt;
                stopped |= stoppedTime >= .25f;
                if (intersection)
                {
                    var facing = driverView.localRotation * Vector3.forward;
                    checkedLeft |= facing.x < -.32f;
                    checkedRight |= facing.x > .32f;
                }
            }
            else stoppedTime = 0f;

            if (front < stopLine) return;
            if (!stopped) End(emergency ? "Pull over and make a complete stop." : "Come to a complete stop before the line.");
            else if (!clear) End("Wait until the conflict area is clear.");
            else if (intersection && (!checkedLeft || !checkedRight)) End("Check left and right before proceeding.");
        }

        void StartRound()
        {
            if (route) Destroy(route);
            course.Clear();
            if (useAuthoredScenario)
            {
                course.Add(authoredScenario);
            }
            else
            {
                route = new GameObject("Practice route");
                var choices = new List<Scenario>((Scenario[])Enum.GetValues(typeof(Scenario)));
                for (var i = 0; i < RoundLength; i++)
                {
                    var pick = Random.Range(0, choices.Count);
                    var section = BuildSection(choices[pick], i);
                    InitializeSection(section);
                    course.Add(section);
                    choices.RemoveAt(pick);
                }
            }
            result = null;
            finished = false;
            completedSessionScenario = false;
            sectionIndex = 0;
            distance = 0;
            vehicle.position = new Vector3(Lane, 0f, 0f);
            speed = 50f;
            ResetSection();
            ShowDrive();
        }

        void ResetSection()
        {
            gear = 2;
            hazardTime = overspeedTime = closeTime = stoppedTime = 0f;
            prompted = stopped = checkedLeft = checkedRight = false;
            var section = course[sectionIndex];
            if (section.Actor) section.Actor.localPosition = section.ActorStart;
            if (section.CrossingChild)
            {
                section.CrossingChild.transform.localPosition = section.ChildStart;
                section.CrossingChild.SetActive(false);
            }
            if (section.Ball) section.Ball.transform.localPosition = section.BallStart;
            if (section.Signal) section.Signal.SetActive(false);
            if (section.StopArm) section.StopArm.SetActive(false);
            if (section.BrakeLights) section.BrakeLights.SetActive(false);
            if (section.Kind == Scenario.Ambulance) section.Actor.gameObject.SetActive(false);
            section.Cue.SetActive(false);
            leadPosition = section.ActorStart.z;
            leadSpeed = 80f;
            speaker.Stop();
            speaker.loop = false;
            speaker.volume = 1f;
        }

        void UpdateView()
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
            var mouse = Mouse.current;
            if (mouse?.rightButton.isPressed == true)
            {
                var delta = mouse.delta.ReadValue();
                yaw = Mathf.Clamp(yaw + delta.x * .17f, -100f, 100f);
                pitch = Mathf.Clamp(pitch - delta.y * .17f, -40f, 40f);
            }
            driverView.localPosition = Vector3.zero;
            driverView.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        static bool Pressed(XRNode node) => InputDevices.GetDeviceAtXRNode(node)
            .TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out var pressed) && pressed;

        void End(string message)
        {
            result = message;
            speaker.Stop();
            speed = 0f;

            completedSessionScenario = finished && useAuthoredScenario &&
                ScenarioLauncher.TryCompleteScenario(gameObject.scene.path);
            if (completedSessionScenario && ScenarioLauncher.HasRemaining)
                StartCoroutine(LoadNextScenario());
        }

        IEnumerator LoadNextScenario()
        {
            yield return new WaitForSecondsRealtime(2f);
            ScenarioLauncher.LoadNext();
        }

        void ShowResult()
        {
            if (completedSessionScenario)
            {
                var progress = $"{ScenarioLauncher.CompletedCount}/{ScenarioLauncher.ScenarioCount}";
                display.text = ScenarioLauncher.HasRemaining
                    ? $"SCENARIO CLEAR   {progress}\nLoading the next scenario…"
                    : $"SESSION COMPLETE   {progress}\nRight button / → for a new session";
                return;
            }

            var retry = useAuthoredScenario ? "retry this scenario" : "start a new round";
            display.text = $"{(finished ? "ROUND COMPLETE" : "TRY AGAIN")}\n{result}\nRight button / → to {retry}";
        }

        void ShowDrive()
        {
            var kind = course[sectionIndex].Kind;
            var instruction = "Left / ← slower      Right / → faster";
            if (kind == Scenario.FlashingRed && prompted && distance - sectionIndex * SectionLength + Nose < StopLinePosition(course[sectionIndex]))
            {
                if (!stopped) instruction = "Flashing red: stop at the line";
                else if (hazardTime < CrossTrafficDuration) instruction = "Wait for cross traffic";
                else if (!checkedLeft || !checkedRight) instruction = "Check left and right";
                else instruction = "Cross traffic clear. Proceed when safe.";
            }
            var progress = useAuthoredScenario && ScenarioLauncher.IsCurrentScenario(gameObject.scene.path)
                ? $"{ScenarioLauncher.CompletedCount + 1}/{ScenarioLauncher.ScenarioCount}"
                : $"{sectionIndex + 1}/{course.Count}";
            display.text = $"{progress}   {Names[(int)kind]}\n" +
                $"{Mathf.RoundToInt(speed)} km/h   ·   Target {Speeds[gear]}\n{instruction}";
        }

        void OnDestroy()
        {
            if (route) Destroy(route);
            foreach (var material in materials) Destroy(material);
            if (prompt) Destroy(prompt);
            if (siren) Destroy(siren);
        }

        Section BuildSection(Scenario kind, int index)
        {
            var root = new GameObject(Names[(int)kind]).transform;
            root.SetParent(route.transform, false);
            root.localPosition = Vector3.forward * (index * SectionLength);
            var section = new Section { Kind = kind };
            var intersection = kind == Scenario.StopSign || kind == Scenario.FlashingRed;
            var expressway = kind == Scenario.Expressway;
            var roadWidth = kind == Scenario.Ambulance ? 11f : 7.4f;
            var leadIn = index == 0 ? 30f : 0f;
            var start = -leadIn;
            var length = SectionLength + leadIn;
            var mid = start + length / 2f;
            Block(root, "Ground", new Vector3(0f, -.23f, mid), new Vector3(150f, .4f, length), grass);
            Block(root, "Road", new Vector3(0f, -.05f, mid), new Vector3(roadWidth, .10f, length), asphalt);
            if (intersection)
                Block(root, "Cross street", new Vector3(0f, -.051f, Conflict + 6f), new Vector3(55f, .10f, 8f), asphalt);

            for (var z = start + 2f; z < SectionLength - 2f; z += 6f)
            {
                if (intersection && z > Conflict && z < Conflict + 12f) continue;
                Block(root, "Centre marking", new Vector3(0f, .008f, z), new Vector3(.12f, .015f, 2.7f), amber);
            }

            foreach (var side in new[] { -1, 1 })
            {
                if (!expressway)
                {
                    var curb = side * (roadWidth / 2f + .8f);
                    var before = intersection ? Conflict + 2f - start : length;
                    Block(root, "Sidewalk", new Vector3(curb, .04f, start + before / 2f),
                        new Vector3(1.55f, .12f, before), sidewalk);
                    if (intersection)
                        Block(root, "Sidewalk", new Vector3(curb, .04f, (Conflict + 10f + SectionLength) / 2f),
                            new Vector3(1.55f, .12f, SectionLength - Conflict - 10f), sidewalk);
                }

                for (var z = 7f; z < SectionLength; z += 13f)
                {
                    var roadside = kind is Scenario.HiddenCrossing or Scenario.Playground or Scenario.SchoolZone;
                    if ((intersection || roadside) && z is > 66f and < 114f) continue;
                    var x = side * (roadWidth / 2f + 3.6f + (z % 4f));
                    if (expressway || Mathf.RoundToInt(z) % 3 == 0)
                    {
                        Block(root, "Tree trunk", new Vector3(x, 1.05f, z), new Vector3(.26f, 2.1f, .26f), bark);
                        Shape(root, "Tree canopy", new Vector3(x, 2.55f, z), new Vector3(2.7f, 3.25f, 2.7f), leaves, PrimitiveType.Sphere);
                    }
                    else
                    {
                        var lot = Mathf.RoundToInt(z / 13f);
                        var source = cityBuildings[(index * 7 + lot * 3 + (side > 0 ? 1 : 0)) % cityBuildings.Count];
                        PlaceCityBuilding(source, root, side, roadWidth / 2f + 3.1f, z);
                    }
                }
            }

            section.Cue = Block(root, "Approach cue", new Vector3(kind == Scenario.Ambulance ? roadWidth / 2f - .16f : Lane * 2f - .16f, .015f,
                    58f + (kind == Scenario.Ambulance ? EmergencyOffset : 0f)),
                new Vector3(.11f, .015f, 22f), amber);
            section.Cue.SetActive(false);

            if (kind != Scenario.SchoolZone && kind != Scenario.Expressway)
                section.StopLine = Block(root, "Stop reference", new Vector3(kind == Scenario.Ambulance ? Lane + 2.55f : Lane, .02f,
                    kind == Scenario.Ambulance ? EmergencyStopLine : Conflict), new Vector3(kind == Scenario.Ambulance ? 2f : 3.35f, .02f, .23f), paint).transform;

            switch (kind)
            {
                case Scenario.StopSign:
                    Place(stopSign, root, 4.03f, Conflict - 1f, 180f);
                    section.Actor = Place(sedan, root, -18f, Conflict + 6f, -90f);
                    break;
                case Scenario.FlashingRed:
                    var signal = Place(trafficLight, root, 3.9f, Conflict + 2f, 180f);
                    section.TrafficSignal = signal;
                    signal.name = "Flashing red signal";
                    section.Actor = Place(sedan, root, -18f, Conflict + 6f, -90f);
                    break;
                case Scenario.HiddenCrossing:
                    section.ParkedCar = Place(sedan, root, 4.02f, Conflict + .1f, 180f);
                    section.ParkedCar.name = "Parked sedan";
                    section.Actor = Place(Random.value < .5f ? pedestrian : cat, root, 5.2f, Conflict + 4f, -90f);
                    break;
                case Scenario.SchoolZone:
                    Place(schoolZone, root, 4.12f, 74f, 180f);
                    Place(limit30, root, 4.12f, 78f, 180f);
                    Place(limit50, root, 4.12f, 129f, 180f);
                    PlaceCityBuilding(schoolBuilding, root, 1, 7.45f, 100f).name = "School";
                    Place(pedestrian, root, 4.5f, 97f, -90f);
                    Place(pedestrian, root, 4.8f, 106f, 20f);
                    break;
                case Scenario.Playground:
                    Place(playground, root, 4.12f, 74f, 180f);
                    Place(limit30, root, 4.12f, 78f, 180f);
                    Place(limit50, root, 4.12f, 129f, 180f);
                    Block(root, "Playground fence", new Vector3(5.6f, .65f, 104f), new Vector3(.12f, 1.3f, 21f), bark);
                    section.Actor = Place(pedestrian, root, 5.2f, Conflict + 4f, -90f);
                    section.Ball = Shape(root, "Rolling ball", new Vector3(5.4f, .16f, Conflict + 3f),
                        Vector3.one * .32f, amber, PrimitiveType.Sphere);
                    break;
                case Scenario.Expressway:
                    Place(limit80, root, 4.12f, 46f, 180f);
                    section.Actor = Place(sedan, root, Lane, 31f, 180f);
                    var lights = new GameObject("Lead brake lights").transform;
                    lights.SetParent(section.Actor, false);
                    foreach (var x in new[] { -.63f, .63f })
                        Block(lights, "Brake light", new Vector3(x, .93f, 1.84f) / section.Actor.localScale.x,
                            new Vector3(.23f, .09f, .04f) / section.Actor.localScale.x, red);
                    section.BrakeLights = lights.gameObject;
                    section.BrakeLights.SetActive(false);
                    break;
                case Scenario.Ambulance:
                    section.Actor = Place(ambulance, root, Lane, 15f + EmergencyOffset, 180f);
                    section.Actor.gameObject.SetActive(false);
                    section.Signal = Shape(root, "Emergency light bar", new Vector3(0f, 2.45f, 0f), new Vector3(.85f, .12f, .27f), red,
                        PrimitiveType.Cube);
                    AttachToActor(section.Signal.transform, section.Actor);
                    break;
                case Scenario.SchoolBus:
                    section.Actor = Place(schoolBus, root, -Lane, 132f, 0f);
                    section.Signal = new GameObject("Flashing bus lights and stop arm");
                    section.Signal.transform.SetParent(section.Actor, false);
                    var scale = section.Actor.localScale.x;
                    foreach (var x in new[] { -.78f, .78f })
                        Shape(section.Signal.transform, "Bus red light", new Vector3(x, 2.71f, 4.98f) / scale,
                            Vector3.one * (.23f / scale), red, PrimitiveType.Sphere);
                    var arm = Place(stopSign, root, -.56f, Conflict + 12f, 180f);
                    foreach (var renderer in arm.GetComponentsInChildren<Renderer>())
                        if (renderer.bounds.min.y < .15f) renderer.enabled = false;
                    arm.SetParent(section.Actor, true);
                    arm.gameObject.SetActive(false);
                    section.StopArm = arm.gameObject;
                    arm.localPosition = new Vector3(-1.42f, .03f, 3.6f) / scale;
                    break;
            }
            if (section.Signal) section.Signal.SetActive(false);
            if (kind == Scenario.SchoolBus)
            {
                section.CrossingChild = Place(pedestrian, root, -5f, Conflict + 4f, 90f).gameObject;
                section.CrossingChild.SetActive(false);
            }
            return section;
        }

        float StopLinePosition(Section section) => section.StopLine
            ? section.StopLine.position.z - sectionIndex * SectionLength
            : (section.Kind == Scenario.Ambulance ? EmergencyStopLine : Conflict);

        bool HitParkedCar(Section section, Vector3 previousPosition)
        {
            // The Sedan moves directly along the route, so sweep its bounds over this step.
            var swept = new Bounds(playerCarBounds.center + previousPosition, playerCarBounds.size);
            swept.Encapsulate(new Bounds(playerCarBounds.center + vehicle.position, playerCarBounds.size));
            var parked = section.ParkedCarBounds;
            parked.center += section.ParkedCar.position;
            return swept.Intersects(parked);
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

        static GameObject PlaceCityBuilding(MeshRenderer source, Transform root, int side, float setback, float z)
        {
            var building = new GameObject(source.name);
            var transform = building.transform;
            transform.SetParent(root, false);
            transform.localRotation = Quaternion.Euler(0f, side * 90f, 0f) * source.transform.rotation;
            transform.localScale = source.transform.lossyScale;
            building.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            var renderer = building.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = source.sharedMaterials;

            // Keep each footprint outside the sidewalk, regardless of its imported pivot.
            var bounds = renderer.bounds;
            var position = root.TransformPoint(new Vector3(side * (setback + bounds.extents.x), 0f, z));
            transform.position += position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            return building;
        }

        static void AttachToActor(Transform light, Transform actor)
        {
            var offset = light.localPosition;
            light.SetParent(actor, true);
            light.localPosition = offset / actor.localScale.x;
        }

        static Transform Place(GameObject prefab, Transform root, float x, float z, float yaw)
        {
            var item = Instantiate(prefab, root).transform;
            item.SetLocalPositionAndRotation(new Vector3(x, 0f, z), Quaternion.Euler(0f, yaw, 0f));
            return item;
        }

        static GameObject Block(Transform root, string title, Vector3 position, Vector3 scale, Material material) =>
            Shape(root, title, position, scale, material, PrimitiveType.Cube);

        static GameObject Shape(Transform root, string title, Vector3 position, Vector3 scale, Material material, PrimitiveType shape)
        {
            var item = GameObject.CreatePrimitive(shape);
            item.name = title;
            Destroy(item.GetComponent<Collider>());
            item.transform.SetParent(root, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            return item;
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
