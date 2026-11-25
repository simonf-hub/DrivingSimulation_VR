using UnityEngine;
using Random = UnityEngine.Random;

namespace BCRoot
{
    // Scenario 2, parked truck (pedestrian or dog): ported from hiddenPed in the prototype's scenarios.js.
    // Numbers: scenarios[id = "hidden_ped"] in Resources/Data/scenario_spec.json. Spec x values are shifted by SpecLaneShift.
    public sealed partial class DrivingPractice
    {
        Transform pedBody;
        bool pedDog, pedCueSaid, pedGo, pedGone, pedCrossed, pedClearSaid, pedDoneSaid, pedSlowAtOut, pedEmergencyOver;
        float pedX, pedPeekClock;

        static PortSpec.ScenarioSpec PedSpec => PortSpec.Current.Scenario("hidden_ped");

        void ResetHiddenCrossing(Section section)
        {
            var spec = PedSpec;
            pedDog = Random.value >= spec.variants.pedestrian.chance;
            pedCueSaid = pedGo = pedGone = pedCrossed = pedClearSaid = pedDoneSaid = pedSlowAtOut = pedEmergencyOver = false;
            pedPeekClock = 0f;
            pedX = spec.startX + SpecLaneShift;
            section.Actor.gameObject.SetActive(!pedDog);
            if (section.Animal) section.Animal.gameObject.SetActive(pedDog);
            pedBody = pedDog && section.Animal ? section.Animal : section.Actor;
            PlacePed();
        }

        void PlacePed()
        {
            var p = pedBody.localPosition;
            pedBody.localPosition = new Vector3(pedX, p.y, PedSpec.stepOutPoint);
        }

        // Returns false when the drive ended (fail) this step.
        bool StepHiddenCrossing(float u, float dt)
        {
            var spec = PedSpec;
            if (u < -40f || u > spec.lengthMetres + 5f) return true;
            var speedMs = pedDog ? spec.variants.dog.speed : spec.variants.pedestrian.speed;

            if (!pedCueSaid && u >= 0f)
            {
                pedCueSaid = true;
                coach.Say("ped_cue");
            }

            // Clue: they peek out from the truck's front now and then.
            if (!pedGo && u >= spec.peek.from)
            {
                pedPeekClock += dt;
                var peeking = pedPeekClock % spec.peek.periodSeconds > spec.peek.periodSeconds - spec.peek.outSeconds;
                pedX = (peeking ? spec.peek.x : spec.startX) + SpecLaneShift;
            }

            if (!pedGo && spec.stepOutPoint - u <= spec.stepOutWhenWithinMetres)
            {
                pedGo = true;
                pedX = spec.startX + SpecLaneShift;
                EmergencyActive = true; // a second Slower tap is queued, not a rear-end
                pedSlowAtOut = Speeds[gear] <= 30;
                coach.Say(pedDog ? "ped_dog" : "ped_out");
            }

            if (pedGo && !pedGone)
            {
                pedX -= speedMs * dt;
                if (pedX < spec.endX + SpecLaneShift)
                {
                    pedGone = true;
                    pedBody.gameObject.SetActive(false);
                }
            }
            if (!pedGone) PlacePed();

            var past = pedGone || pedX <= spec.clearAtX + SpecLaneShift;
            var inPath = pedGo && !past; // stepping out or crossing our lane
            var onRoad = inPath && pedX < spec.inRoadBelowX + SpecLaneShift;
            scenarioHold = inPath;
            if (pedGo && past && !pedEmergencyOver)
            {
                pedEmergencyOver = true;
                EmergencyActive = false;
            }

            if (!pedCrossed && u > spec.stepOutPoint - spec.hitCheckBefore)
            {
                if (onRoad)
                {
                    // Already slow when they stepped out: the lesson is to stop, not to slow earlier.
                    Fail(pedDog ? "hit_animal" : "hit_pedestrian",
                        pedSlowAtOut ? "ped_fail_late" : pedDog ? "ped_fail_dog" : "ped_fail_hit");
                    return false;
                }
                pedCrossed = true;
            }

            if (pedGo && past && !pedClearSaid && !pedCrossed)
            {
                pedClearSaid = true;
                coach.Say(pedDog ? "ped_dog_clear" : "ped_clear");
            }
            if (pedCrossed && !pedDoneSaid && u > spec.doneAt)
            {
                pedDoneSaid = true;
                coach.Say("ped_done");
            }
            return true;
        }
    }
}
