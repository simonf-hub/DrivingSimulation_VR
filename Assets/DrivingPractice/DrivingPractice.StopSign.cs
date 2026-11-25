using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace BCRoot
{
    // Scenarios 1 and 6, stop sign and flashing red light: ported from makeIntersection() in the prototype's scenarios.js.
    // Numbers: scenarios[id = "stop"] in Resources/Data/scenario_spec.json (flashing_red is "sameAs": "stop").
    // u = front bumper metres past the section start.
    public sealed partial class DrivingPractice
    {
        // Spec x positions assume a 2.1 m lane centre; the scene's lane is at Lane (1.85).
        const float SpecLaneShift = Lane - 2.1f;
        const float CrossCarHalfLength = 2.2f, CrossCarHalfWidth = .9f;

        sealed class CrossCar
        {
            public Transform Body;
            public int Dir; // +1 drives toward +x (comes from the left), -1 toward -x (from the right)
            public float X, Z, Speed;
            public float Back => X - Dir * CrossCarHalfLength;
            public float Near => X + Dir * CrossCarHalfLength;
        }

        sealed class PlannedCar
        {
            public float At, X;
            public int Dir;
        }

        readonly List<CrossCar> crossCars = new();
        readonly List<PlannedCar> crossPlan = new(), crossSchedule = new();
        bool stopCueSaid, hintSent, stopMade, entered, hitChecked, stopDoneSaid, tooFarSaid, lookedLeft, lookedRight;
        float stopClock, flashClock;

        static PortSpec.ScenarioSpec StopSpec => PortSpec.Current.Scenario("stop");

        void ResetStopSign(Section section)
        {
            foreach (var car in crossCars) Destroy(car.Body.gameObject);
            crossCars.Clear();
            crossSchedule.Clear();
            stopCueSaid = hintSent = stopMade = entered = hitChecked = stopDoneSaid = tooFarSaid = lookedLeft = lookedRight = false;
            stopClock = flashClock = 0f;
            section.Actor.gameObject.SetActive(false); // the scene's sedan is only the template for cross traffic

            // This attempt's cross traffic: 1-3 cars, random sides and gaps, sometimes a late car.
            var traffic = StopSpec.crossTraffic;
            var roll = Random.value;
            var count = roll < traffic.countWeights[0] ? 1 : roll < traffic.countWeights[0] + traffic.countWeights[1] ? 2 : 3;
            var t = 0f;
            crossPlan.Clear();
            for (var i = 0; i < count; i++)
            {
                var dir = Random.value < .5f ? 1 : -1;
                var first = i == 0;
                var late = !first && i == count - 1 && Random.value < traffic.lastCarLateChance;
                t += first ? 0f : late ? Range(traffic.lateCarDelay) : Range(traffic.laterCarDelay);
                // The first car is already on its way when you stop: you only see it if you look.
                var d = first ? Range(traffic.firstCarDistance) : traffic.laterCarDistance;
                crossPlan.Add(new PlannedCar { At = t, Dir = dir, X = -dir * d });
            }
        }

        static float Range(PortSpec.Range r) => Random.Range(r.from, r.to);

        float CrossLaneZ(int dir) => StopSpec.intersectionCentre + (dir > 0 ? -1f : 1f) * StopSpec.crossTraffic.laneOffset; // drive on the right

        void SpawnCrossCar(Section section, int dir, float x, float speedMs)
        {
            var body = Instantiate(section.Actor, section.Actor.parent);
            body.name = "Cross traffic";
            body.gameObject.SetActive(true);
            var car = new CrossCar { Body = body, Dir = dir, X = x, Z = CrossLaneZ(dir), Speed = speedMs };
            body.localRotation = Quaternion.Euler(0f, dir > 0 ? 270f : 90f, 0f); // the model's front is its -Z
            PlaceCrossCar(car);
            crossCars.Add(car);
        }

        static void PlaceCrossCar(CrossCar car)
        {
            var p = car.Body.localPosition;
            car.Body.localPosition = new Vector3(car.X, p.y, car.Z);
        }

        // Path strip the player crosses, in scene x.
        static float PathFrom => StopSpec.conflict.pathX.from + SpecLaneShift;
        static float PathTo => StopSpec.conflict.pathX.to + SpecLaneShift;

        bool NotPastPath(CrossCar c) => c.Dir > 0 ? c.Back < PathTo : c.Back > PathFrom;

        // A crossing car conflicts if it is in our path now, or would reach it before we are through.
        bool Conflicts(CrossCar c, float u)
        {
            if (c.Dir > 0 ? c.Near > PathFrom && c.Back < PathTo : c.Near < PathTo && c.Back > PathFrom) return true;
            var coming = c.Dir > 0 ? c.Near <= PathFrom : c.Near >= PathTo;
            if (!coming) return false;
            var tArrive = (c.Dir > 0 ? PathFrom - c.Near : c.Near - PathTo) / c.Speed;
            var dClear = CrossLaneZ(c.Dir) + .9f + 4.6f - u; // metres until our rear clears its lane
            var v = Mathf.Max(speed / 3.6f, StopSpec.conflict.minClearSpeed);
            return tArrive < dClear / v + StopSpec.conflict.marginSeconds;
        }

        float GapToPath(CrossCar c) => c.Dir > 0 ? PathFrom - c.Near : c.Near - PathTo;

        bool OverlapsCrossCar(CrossCar c)
        {
            var player = new Bounds(playerCarBounds.center + vehicle.localPosition - Vector3.forward * worldStart, playerCarBounds.size);
            var other = new Bounds(new Vector3(c.X, player.center.y, c.Z),
                new Vector3(CrossCarHalfLength * 2f, player.size.y, CrossCarHalfWidth * 2f));
            return player.Intersects(other);
        }

        // Returns false when the drive ended (fail) this step.
        bool StepStopSign(Section section, float u, float dt)
        {
            var spec = StopSpec;
            var isLight = section.Kind == Scenario.FlashingRed;
            if (isLight) FlashSignal(section, dt);
            for (var i = crossCars.Count - 1; i >= 0; i--)
            {
                var car = crossCars[i];
                car.X += car.Speed * dt * car.Dir;
                PlaceCrossCar(car);
                if (Mathf.Abs(car.X) <= 80f) continue;
                Destroy(car.Body.gameObject);
                crossCars.RemoveAt(i);
            }
            if (u < -40f || u > spec.lengthMetres + 5f) return true;

            if (!stopCueSaid && u >= spec.cueAt)
            {
                stopCueSaid = true;
                coach.Say(isLight ? "fr_cue" : "stop_cue");
            }
            if (!hintSent && u >= spec.hintCar.at)
            {
                hintSent = true; // a car crossing ahead: a hint that this street is busy
                SpawnCrossCar(section, -1, spec.hintCar.startDistance, spec.hintCar.speed);
            }

            if (!stopMade)
            {
                var stoppedNow = speed <= 0f;
                if (stoppedNow && u >= spec.validStop.from && u <= spec.validStop.to)
                {
                    stopMade = true;
                    stopClock = 0f;
                    crossSchedule.Clear();
                    crossSchedule.AddRange(crossPlan);
                    coach.Say(spec.stopLine - u <= spec.stopLookWithinMetres ? "stop_look" : "stop_early");
                }
                else if (stoppedNow && u < spec.stopLine - spec.stopTooFar.beforeLineMetres &&
                    stoppedFor > spec.stopTooFar.stoppedSeconds && !tooFarSaid)
                {
                    tooFarSaid = true;
                    coach.Say("stop_toofar");
                }
                else if (u > spec.validStop.to)
                {
                    Fail(isLight ? "rolled_flashing_red" : "rolled_stop", isLight ? "fr_fail_rolled" : "stop_fail_rolled");
                    return false;
                }
                TrackLooking();
                return true;
            }

            stopClock += dt;
            for (var i = 0; i < crossSchedule.Count; i++)
            {
                if (stopClock < crossSchedule[i].At) continue;
                SpawnCrossCar(section, crossSchedule[i].Dir, crossSchedule[i].X, spec.crossTraffic.speed);
                crossSchedule.RemoveAt(i--);
            }
            TrackLooking();

            // Keep the beaver's "go" nudge quiet while any car is still to come or hasn't passed our lane.
            scenarioHold = !entered && (crossSchedule.Count > 0 || crossCars.Exists(NotPastPath));

            if (!entered && u > spec.enteredAfter)
            {
                entered = true;
                scenarioHold = false;
                var conflict = crossCars.Find(c => Conflicts(c, u));
                if (conflict != null)
                {
                    Fail("entered_with_traffic", "stop_fail_traffic", conflict.Dir > 0 ? "left" : "right",
                        Mathf.Max(0, Mathf.RoundToInt(GapToPath(conflict))));
                    return false;
                }
            }

            if (entered && !hitChecked)
            {
                if (crossCars.Exists(OverlapsCrossCar))
                {
                    Fail("hit_cross_traffic", "stop_fail_hit");
                    return false;
                }
                if (u > spec.hitCheckUntil) hitChecked = true;
            }

            if (!stopDoneSaid && u > spec.doneAt)
            {
                stopDoneSaid = true;
                coach.Say(lookedLeft && lookedRight ? isLight ? "fr_done" : "stop_done" : "stop_tip");
            }
            return true;
        }

        // Red lens on for the first half of each period, on game time (it freezes while a card is up).
        void FlashSignal(Section section, float dt)
        {
            flashClock += dt;
            var period = PortSpec.Current.Scenario("flashing_red").flashPeriodSeconds;
            var on = flashClock % period < period / 2f;
            if (!section.RedLensRenderer || section.RedLensLit == on) return;
            section.RedLensLit = on;
            section.RedLensRenderer.sharedMaterials = on ? section.RedLensOn : section.RedLensOff;
        }

        // Looking counts while stopped: more than lookedSideDegreesWhileStopped to each side.
        void TrackLooking()
        {
            if (stoppedFor <= 0f || entered) return;
            var forward = driverView.localRotation * Vector3.forward;
            var heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg; // right = positive
            var threshold = PortSpec.Current.engine.look.lookedSideDegreesWhileStopped;
            lookedLeft |= heading < -threshold;
            lookedRight |= heading > threshold;
        }
    }
}
