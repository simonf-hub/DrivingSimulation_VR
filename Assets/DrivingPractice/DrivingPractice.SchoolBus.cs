using System.Collections.Generic;
using UnityEngine;

namespace BCRoot
{
    // Scenario 8, oncoming school bus: ported from schoolBus in the prototype's scenarios.js.
    // Numbers: scenarios[id = "school_bus"] in Resources/Data/scenario_spec.json. Spec x values are shifted by SpecLaneShift.
    public sealed partial class DrivingPractice
    {
        readonly List<Transform> busRedLights = new(), busAmberLights = new();
        bool busCueSaid, busRed, busOff, busGone, busChildOut, busChildGone, busDoneSaid, busClearTimed;
        float busClock, busFront, busStart, busLeaveClock, busLeaveSpeed, busClearAt, busFrontOffset, busChildX, busFlashClock;

        static PortSpec.ScenarioSpec BusSpec => PortSpec.Current.Scenario("school_bus");

        void ResetSchoolBus(Section section)
        {
            busCueSaid = busRed = busOff = busGone = busChildOut = busChildGone = busDoneSaid = busClearTimed = false;
            busClock = busLeaveClock = busLeaveSpeed = busFlashClock = 0f;
            var bus = section.Actor;
            bus.gameObject.SetActive(true);
            // The bus faces us (front toward -z); measure where its front bumper is relative to its origin.
            busFrontOffset = CarBoundsRelativeTo(bus).min.z;

            // Red lights come with the scene; add amber ones just inboard of them (real buses flash amber while stopping).
            busRedLights.Clear();
            if (section.Signal)
                foreach (Transform light in section.Signal.transform)
                    if (light.name == "Bus red light") busRedLights.Add(light);
            if (busAmberLights.Count == 0 && busRedLights.Count > 0)
            {
                var amberLens = Surface(new Color(1f, .62f, .05f), true);
                foreach (var red in busRedLights)
                {
                    var amber = Instantiate(red, red.parent);
                    amber.name = "Bus amber light";
                    var p = red.localPosition;
                    amber.localPosition = new Vector3(p.x * .5f, p.y, p.z);
                    amber.GetComponent<Renderer>().sharedMaterial = amberLens;
                    busAmberLights.Add(amber);
                }
            }
            if (section.Signal) section.Signal.SetActive(true);
            SetBusLights(false, false);
            if (section.StopArm) section.StopArm.SetActive(false);
            if (section.CrossingChild) section.CrossingChild.SetActive(false);

            var spec = BusSpec;
            busStart = spec.bus.stopFrontAt + spec.bus.startAheadOfStop;
            busFront = busStart;
            PlaceBus(section);
        }

        void PlaceBus(Section section)
        {
            var p = section.Actor.localPosition;
            section.Actor.localPosition = new Vector3(p.x, p.y, busFront - busFrontOffset);
        }

        // Alternating left/right flash; amber while the bus slows, red while it is stopped with the arm out.
        void SetBusLights(bool amber, bool red)
        {
            var period = BusSpec.bus.lightFlashPeriodSeconds;
            var phase = busFlashClock % period < period / 2f;
            for (var i = 0; i < busRedLights.Count; i++) busRedLights[i].gameObject.SetActive(red && phase == (i % 2 == 0));
            for (var i = 0; i < busAmberLights.Count; i++) busAmberLights[i].gameObject.SetActive(amber && phase == (i % 2 == 0));
        }

        bool StepSchoolBus(Section section, float u, float dt)
        {
            var spec = BusSpec;
            if (u < -12f || u > spec.lengthMetres + 40f) return true;
            if (!busCueSaid)
            {
                busCueSaid = true;
                coach.Say("bus_cue");
            }
            busClock += dt;
            busFlashClock += dt;

            if (busGone)
            {
                if (!busDoneSaid && u > spec.doneAt)
                {
                    busDoneSaid = true;
                    coach.Say("bus_done");
                }
                return true;
            }

            var bus = spec.bus;
            if (busClock < bus.stopSeconds)
            {
                var decel = bus.startSpeed / bus.stopSeconds; // comes to a stop over stopSeconds
                busFront = busStart - (bus.startSpeed * busClock - .5f * decel * busClock * busClock);
            }
            else if (busOff)
            {
                busLeaveClock += dt;
                if (busLeaveClock > bus.leaveDelaySeconds)
                {
                    busLeaveSpeed = Mathf.Min(bus.leaveMaxSpeed, busLeaveSpeed + bus.leaveAccel * dt);
                    busFront -= busLeaveSpeed * dt;
                }
                if (busFront < u - 45f)
                {
                    busGone = true;
                    section.Actor.gameObject.SetActive(false);
                }
            }
            else
            {
                busFront = bus.stopFrontAt;
                if (!busRed)
                {
                    busRed = true;
                    if (section.StopArm) section.StopArm.SetActive(true);
                    coach.Say("bus_red");
                }
            }
            if (!busGone) PlaceBus(section);
            SetBusLights(busClock < bus.stopSeconds, busRed);

            // The child crosses in front of the bus, all the way to our curb.
            var child = spec.child;
            if (busClock >= child.atBusSeconds && !busChildOut && !busChildGone && section.CrossingChild)
            {
                busChildOut = true;
                busChildX = child.startX + SpecLaneShift;
                section.CrossingChild.SetActive(true);
                section.CrossingChild.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // faces +x (model front is +Z)
            }
            if (busChildOut && !busChildGone)
            {
                busChildX += child.speed * dt;
                var c = section.CrossingChild.transform;
                c.localPosition = new Vector3(busChildX, c.localPosition.y, bus.stopFrontAt + child.aheadOfBus);
                if (busChildX > child.clearAtX + SpecLaneShift && !busClearTimed)
                {
                    busClearTimed = true;
                    busClearAt = busClock;
                }
                if (busChildX > child.goneAtX + SpecLaneShift)
                {
                    busChildGone = true;
                    section.CrossingChild.SetActive(false);
                }
            }
            if (busRed && busClearTimed && busClock >= busClearAt + spec.lightsOffAfterClearSeconds)
            {
                busRed = false;
                busOff = true;
                if (section.StopArm) section.StopArm.SetActive(false);
                coach.Say("bus_clear");
            }
            scenarioHold = busRed || busChildOut && !busChildGone;

            if (u > spec.noPassPoint && !busOff && busClock >= bus.stopSeconds)
            {
                Fail("passed_school_bus", "bus_fail");
                return false;
            }
            if (!busDoneSaid && u > spec.doneAt)
            {
                busDoneSaid = true;
                coach.Say("bus_done");
            }
            return true;
        }
    }
}
