using UnityEngine;

namespace BCRoot
{
    // Scenario 5, 80 zone with a braking car ahead: ported from leadCar in the prototype's scenarios.js.
    // Numbers: scenarios[id = "lead_car"] in Resources/Data/scenario_spec.json. Speeds below are m/s unless named Kmh.
    public sealed partial class DrivingPractice
    {
        bool leadSpawned, leadDone, leadCueSaid, leadDoneSaid, leadReached80;
        int leadPhase; // 0 cruising, 1 braking, 2 waiting, 3 speeding away
        float leadRear, leadSpeedMs, leadPhaseClock, leadBrakeTarget, leadCloseTime, leadRearOffset;

        static PortSpec.ScenarioSpec LeadSpec => PortSpec.Current.Scenario("lead_car");

        void ResetLeadCar(Section section)
        {
            leadSpawned = leadDone = leadCueSaid = leadDoneSaid = leadReached80 = false;
            leadPhase = 0;
            leadPhaseClock = leadCloseTime = 0f;
            // Distance from the model's origin back to its rear bumper, so the gap is bumper to bumper.
            // Measured before hiding it: an inactive renderer reports empty bounds.
            section.BrakeLights.SetActive(false);
            leadRearOffset = -CarBoundsRelativeTo(section.Actor).min.z;
            section.Actor.gameObject.SetActive(false);
        }

        void PlaceLeadCar(Section section)
        {
            var p = section.Actor.localPosition;
            section.Actor.localPosition = new Vector3(p.x, p.y, leadRear + leadRearOffset);
        }

        bool StepLeadCar(Section section, float u, float dt)
        {
            var spec = LeadSpec;
            if (u < -12f) return true;
            if (!leadSpawned && !leadDone && u < spec.lengthMetres)
            {
                leadSpawned = true;
                leadRear = u + spec.leadCar.startAhead;
                leadSpeedMs = spec.leadCar.startKmh / 3.6f;
                section.Actor.gameObject.SetActive(true);
            }
            if (!leadCueSaid && u >= spec.cueAt)
            {
                leadCueSaid = true;
                coach.Say("lead_cue");
            }
            if (!leadSpawned || leadDone) return true;

            var car = spec.leadCar;
            var brake = spec.brake;
            leadPhaseClock += dt;
            if (leadPhase == 0)
            {
                // Stay in view about 45-70 m ahead: speed up when we close in, ease off when we fall behind,
                // but not below 60 km/h until the gap passes 55 m, so a player doing 80 meets a car that then brakes.
                var g0 = leadRear - u;
                var lower = Mathf.Clamp(car.floorKmh.to - (g0 - car.floorGapStart) * car.floorGain, car.floorKmh.from, car.floorKmh.to) / 3.6f;
                leadSpeedMs = Mathf.Min(car.maxKmh / 3.6f, Mathf.Max(lower, speed / 3.6f - (g0 - car.cruiseTargetGap) * car.cruiseGain));
                if (u >= brake.atU || leadRear - u <= brake.orGapAtMost)
                {
                    leadPhase = 1;
                    leadPhaseClock = 0f;
                    section.BrakeLights.SetActive(true);
                    leadBrakeTarget = Mathf.Clamp(leadSpeedMs * 3.6f - brake.dropKmh, brake.targetKmh.from, brake.targetKmh.to) / 3.6f;
                    coach.Say("lead_brake");
                }
            }
            else if (leadPhase == 1)
            {
                leadSpeedMs = Mathf.Max(leadBrakeTarget, leadSpeedMs - brake.decel * dt);
                if (leadSpeedMs <= leadBrakeTarget + .01f && leadPhaseClock > brake.holdSeconds)
                {
                    leadPhase = 2;
                    leadPhaseClock = 0f;
                    section.BrakeLights.SetActive(false);
                }
            }
            else if (leadPhase == 2)
            {
                if (leadPhaseClock > brake.waitSeconds)
                {
                    leadPhase = 3;
                    leadPhaseClock = 0f;
                }
            }
            else leadSpeedMs = Mathf.Min(car.maxKmh / 3.6f, leadSpeedMs + brake.accel * dt);

            leadRear += leadSpeedMs * dt;
            PlaceLeadCar(section);
            var gap = leadRear - u;
            if (leadPhase == 3 && gap > spec.removeWhenGapOver || leadRear > spec.lengthMetres + 120f)
            {
                leadDone = true;
                section.Actor.gameObject.SetActive(false);
                return true;
            }
            if (u > spec.lengthMetres) return true;

            if (speed >= spec.reached80AtKmh) leadReached80 = true;
            if (gap <= 0f)
            {
                Fail("hit_lead_car", "lead_fail_hit");
                return false;
            }
            var v = speed / 3.6f;
            if (v > spec.headway.minPlayerSpeed)
            {
                leadCloseTime = gap / v < spec.headway.minSeconds ? leadCloseTime + dt : 0f;
                if (leadCloseTime > spec.headway.graceSeconds)
                {
                    Fail("too_close", "lead_fail_close");
                    return false;
                }
            }
            if (!leadDoneSaid && u > spec.doneAt)
            {
                leadDoneSaid = true;
                coach.Say(leadReached80 ? "lead_done" : "lead_done_slow");
            }
            return true;
        }
    }
}
