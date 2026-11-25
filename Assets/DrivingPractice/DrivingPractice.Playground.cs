using UnityEngine;

namespace BCRoot
{
    // Scenario 4, playground zone (ball, then child): ported from playground in the prototype's scenarios.js.
    // Numbers: scenarios[id = "playground"] in Resources/Data/scenario_spec.json. Spec x values are shifted by SpecLaneShift.
    public sealed partial class DrivingPractice
    {
        bool pgCueSaid, pgGo, pgBallGone, pgChildGone, pgCrossed, pgClearSaid, pgDoneSaid, pgEmergencyOver;
        float pgClock, pgBob;
        Vector2 pgChild; // x across the road, z along it (section-local)

        static PortSpec.ScenarioSpec PlaygroundSpec => PortSpec.Current.Scenario("playground");

        void ResetPlayground(Section section)
        {
            var spec = PlaygroundSpec;
            pgCueSaid = pgGo = pgBallGone = pgChildGone = pgCrossed = pgClearSaid = pgDoneSaid = pgEmergencyOver = false;
            pgClock = pgBob = 0f;
            // Playing well to the left: look left to see the child early.
            pgChild = new Vector2(spec.child.startX + SpecLaneShift, spec.crossingPoint - spec.child.startBehind);
            section.Actor.gameObject.SetActive(true);
            section.Actor.localRotation = Quaternion.Euler(0f, 90f, 0f); // faces +x, the way it will run (model front is +Z)
            PlaceChild(section);
            section.Ball.SetActive(false);
        }

        void PlaceChild(Section section)
        {
            var p = section.Actor.localPosition;
            section.Actor.localPosition = new Vector3(pgChild.x, p.y, pgChild.y);
        }

        bool StepPlayground(Section section, float u, float dt)
        {
            var spec = PlaygroundSpec;
            if (u < -40f || u > spec.lengthMetres + 5f) return true;

            if (!pgCueSaid && u >= spec.cueAt)
            {
                pgCueSaid = true;
                coach.Say("pg_cue");
            }
            if (!SpeedZone(u, dt, spec.zone, "pg_fail_speed")) return false;

            if (!pgGo)
            {
                pgBob += dt; // child playing
                pgChild.x = spec.child.startX + SpecLaneShift + Mathf.Sin(pgBob * 1.3f) * .6f;
            }
            if (!pgGo && spec.crossingPoint - u <= spec.triggerWithinMetres)
            {
                pgGo = true;
                section.Ball.SetActive(true);
                var b = section.Ball.transform.localPosition;
                section.Ball.transform.localPosition = new Vector3(spec.ball.startX + SpecLaneShift, b.y, spec.crossingPoint + .6f);
                EmergencyActive = true;
                coach.Say("pg_ball");
            }

            if (pgGo)
            {
                pgClock += dt;
                if (!pgBallGone)
                {
                    var b = section.Ball.transform.localPosition;
                    b.x += spec.ball.speed * dt;
                    section.Ball.transform.localPosition = b;
                    if (b.x > spec.ball.goneAtX + SpecLaneShift)
                    {
                        pgBallGone = true;
                        section.Ball.SetActive(false);
                    }
                }
                if (pgClock >= spec.child.startDelaySeconds && !pgChildGone)
                {
                    var edge = new Vector2(spec.child.roadEdgeX + SpecLaneShift, spec.crossingPoint);
                    if (pgChild.y < spec.crossingPoint - .01f || pgChild.x < edge.x)
                        pgChild = Vector2.MoveTowards(pgChild, edge, spec.child.runToRoadSpeed * dt); // runs to the road
                    else
                    {
                        pgChild.x += spec.child.crossSpeed * dt;
                        if (pgChild.x > spec.child.goneAtX + SpecLaneShift)
                        {
                            pgChildGone = true;
                            section.Actor.gameObject.SetActive(false);
                        }
                    }
                }
            }
            if (!pgChildGone) PlaceChild(section);

            var busy = pgGo && !pgChildGone && pgChild.x < spec.child.clearAtX + SpecLaneShift;
            var childClear = pgGo && !busy;
            scenarioHold = busy;
            if (childClear && !pgEmergencyOver)
            {
                pgEmergencyOver = true;
                EmergencyActive = false;
            }

            if (!pgCrossed && u > spec.crossingPoint - spec.hitCheckBefore)
            {
                if (busy)
                {
                    Fail("entered_with_child", "pg_fail_child");
                    return false;
                }
                pgCrossed = true;
            }
            if (childClear && !pgClearSaid && !pgCrossed)
            {
                pgClearSaid = true;
                coach.Say("pg_clear");
            }
            if (!pgDoneSaid && u > spec.doneAt)
            {
                pgDoneSaid = true;
                coach.Say("pg_done");
            }
            return true;
        }
    }
}
