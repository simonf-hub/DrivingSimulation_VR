using System.Collections.Generic;
using UnityEngine;

namespace BCRoot
{
    // Scenario 3, school zone: ported from schoolZone in the prototype's scenarios.js.
    // Numbers: scenarios[id = "school"] in Resources/Data/scenario_spec.json.
    public sealed partial class DrivingPractice
    {
        readonly List<Transform> walkers = new(), walkerClones = new();
        readonly List<float> walkerSpeeds = new();
        bool schoolCueSaid, schoolEndSaid, schoolDoneSaid;
        float zoneOverTime;

        static PortSpec.ScenarioSpec SchoolSpec => PortSpec.Current.Scenario("school");

        void ResetSchoolZone()
        {
            schoolCueSaid = schoolEndSaid = schoolDoneSaid = false;
            foreach (var clone in walkerClones) Destroy(clone.gameObject);
            walkerClones.Clear();
            walkers.Clear();
            walkerSpeeds.Clear();

            // The scene's people, topped up with copies to the spec's count.
            if (!layout) return;
            foreach (Transform child in layout)
                if (child.name == "Character") walkers.Add(child);
            if (walkers.Count == 0) return;
            var people = SchoolSpec.sidewalkPeople;
            while (walkers.Count < people.count)
            {
                var clone = Instantiate(walkers[0], layout);
                clone.name = "Character (walker)";
                walkerClones.Add(clone);
                walkers.Add(clone);
            }
            for (var i = 0; i < walkers.Count; i++)
            {
                // shortcut: the prototype walks them at x 6.0-7.4, which is past this scene's sidewalk (3.7-5.3); kept on the sidewalk.
                var x = 4.1f + i % 3 * .4f;
                walkers[i].localPosition = new Vector3(x, 0f, people.firstAt + i * people.spacing);
                walkerSpeeds.Add(i % 2 == 1 ? .7f : -.5f);
                FaceWalker(i);
            }
        }

        void FaceWalker(int i) => walkers[i].localRotation = Quaternion.Euler(0f, walkerSpeeds[i] > 0f ? 0f : 180f, 0f); // model front is +Z

        bool StepSchoolZone(float u, float dt)
        {
            var spec = SchoolSpec;
            var people = spec.sidewalkPeople;
            for (var i = 0; i < walkers.Count; i++)
            {
                var p = walkers[i].localPosition;
                p.z += walkerSpeeds[i] * dt;
                if (p.z < people.from || p.z > people.to)
                {
                    walkerSpeeds[i] = -walkerSpeeds[i];
                    FaceWalker(i);
                }
                walkers[i].localPosition = p;
            }
            if (u < -40f || u > spec.lengthMetres + 5f) return true;

            if (!schoolCueSaid && u >= spec.cueAt)
            {
                schoolCueSaid = true;
                coach.Say("school_cue");
            }
            if (!SpeedZone(u, dt, spec.zone, "school_fail_speed")) return false;
            if (!schoolEndSaid && u > spec.endAt)
            {
                schoolEndSaid = true;
                coach.Say("school_end");
            }
            if (!schoolDoneSaid && u > spec.doneAt)
            {
                schoolDoneSaid = true;
                coach.Say("school_done");
            }
            return true;
        }

        // A posted zone (school, playground): over its limit with the front bumper inside it for longer than its grace.
        bool SpeedZone(float u, float dt, PortSpec.ZoneSpec zone, string lineId)
        {
            if (u < zone.from || u > zone.to)
            {
                zoneOverTime = 0f;
                return true;
            }
            var tolerance = PortSpec.Current.engine.speedLimit.toleranceKmh;
            zoneOverTime = speed > zone.limitKmh + tolerance ? zoneOverTime + dt : 0f;
            if (zoneOverTime <= zone.graceSeconds) return true;
            Fail("speeding", lineId);
            return false;
        }
    }
}
