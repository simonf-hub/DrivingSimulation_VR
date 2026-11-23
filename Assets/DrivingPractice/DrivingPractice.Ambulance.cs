using UnityEngine;

namespace BCRoot
{
    // Scenario 7, ambulance from behind: ported from ambulance in the prototype's scenarios.js.
    // Numbers: scenarios[id = "ambulance"] in Resources/Data/scenario_spec.json. Spec x values are shifted by SpecLaneShift.
    public sealed partial class DrivingPractice
    {
        bool ambSpawned, ambDone, ambPassed, ambArrived, ambStopped, ambPulled, ambBack, ambDoneSaid;
        float ambFront, ambX, ambLength, ambFrontOffset, ambFlashClock;

        static PortSpec.ScenarioSpec AmbSpec => PortSpec.Current.Scenario("ambulance");

        void ResetAmbulance(Section section)
        {
            ambSpawned = ambDone = ambPassed = ambArrived = ambStopped = ambPulled = ambBack = ambDoneSaid = false;
            ambFlashClock = 0f;
            // The model's front is +Z; measure it before hiding it (an inactive renderer reports empty bounds).
            var bounds = CarBoundsRelativeTo(section.Actor);
            ambFrontOffset = bounds.max.z;
            ambLength = bounds.size.z;
            section.Actor.gameObject.SetActive(false);
        }

        void PlaceAmbulance(Section section)
        {
            var p = section.Actor.localPosition;
            section.Actor.localPosition = new Vector3(ambX, p.y, ambFront - ambFrontOffset);
        }

        bool StepAmbulance(Section section, float u, float dt)
        {
            var spec = AmbSpec;
            if (u < -30f) return true;
            var rear = u - Nose + playerCarBounds.min.z; // our rear bumper

            if (!ambSpawned && !ambDone && u >= spec.spawnAt && u < spec.lengthMetres)
            {
                ambSpawned = ambPulled = true;
                ambFront = rear - spec.spawnBehind;
                ambX = Lane;
                section.Actor.gameObject.SetActive(true);
                PlaceAmbulance(section);
                EmergencyActive = true; // a second Slower tap is queued, not a rear-end
                MirrorAlert = true;
                carXTarget = spec.pullOver.carX + SpecLaneShift; // the car eases onto the shoulder
                speaker.clip = siren;
                speaker.loop = true;
                speaker.Play();
                coach.Say("amb_cue");
            }
            if (ambPulled && !ambBack && (ambDone || u > spec.lengthMetres - 5f))
            {
                ambBack = true;
                carXTarget = spec.pullOver.returnX + SpecLaneShift;
            }
            if (!ambSpawned || ambDone) return true;

            // Light bar flashes; the siren gets louder as it closes in.
            ambFlashClock += dt;
            if (section.Signal) section.Signal.SetActive(ambFlashClock % spec.flashPeriodSeconds < spec.flashPeriodSeconds / 2f);
            if (!ambPassed) speaker.volume = Mathf.Lerp(.4f, .08f, Mathf.Clamp01((rear - ambFront) / spec.spawnBehind));

            ambFront += (ambPassed ? spec.passedSpeed : Mathf.Max(speed / 3.6f + spec.closingSpeed.abovePlayer, spec.closingSpeed.min)) * dt;
            if (rear - ambFront < spec.goesAroundWithinMetres)
                ambX += (spec.goesAroundX + SpecLaneShift - ambX) * Mathf.Min(1f, dt * 1.5f); // moves left to go around us
            PlaceAmbulance(section);

            if (ambPassed)
            {
                scenarioHold = false;
                if (ambPulled && !ambBack && speed > .5f)
                {
                    ambBack = true;
                    carXTarget = spec.pullOver.returnX + SpecLaneShift;
                }
                if (ambFront - u > 220f)
                {
                    ambDone = true;
                    section.Actor.gameObject.SetActive(false);
                }
                if (!ambDoneSaid && u > spec.doneAt)
                {
                    ambDoneSaid = true;
                    coach.Say("amb_done");
                }
                return true;
            }

            if (!ambStopped && speed <= 0f) ambStopped = true;
            scenarioHold = ambStopped;
            if (ambStopped && speed > .5f)
            {
                Fail("moved_too_soon", "amb_fail_moved");
                return false;
            }
            if (u > spec.pullOver.to && speed > .5f)
            {
                Fail("did_not_pull_over", "amb_fail_nopull");
                return false;
            }
            if (!ambArrived && ambFront >= rear)
            {
                ambArrived = true;
                if (carX < spec.pullOver.requiredX + SpecLaneShift)
                {
                    Fail("blocked_ambulance", "amb_fail_blocked");
                    return false;
                }
                if (speed > .5f)
                {
                    Fail("not_stopped_for_ambulance", "amb_fail_notstopped");
                    return false;
                }
            }
            if (ambFront - ambLength > u + spec.passedAheadMetres)
            {
                ambPassed = true;
                EmergencyActive = MirrorAlert = false;
                speaker.Stop();
                speaker.loop = false;
                coach.Say("amb_passed");
            }
            return true;
        }
    }
}
