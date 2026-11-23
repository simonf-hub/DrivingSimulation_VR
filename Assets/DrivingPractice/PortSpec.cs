using System;
using UnityEngine;

namespace BCRoot
{
    // Tuning numbers ported from the web prototype. Source: Resources/Data/scenario_spec.json.
    // Only the fields the code reads so far are declared; JsonUtility ignores the rest.
    [Serializable]
    internal sealed class PortSpec
    {
        public EngineSpec engine;
        public ModesSpec modes;
        public ScenarioSpec[] scenarios;

        internal ScenarioSpec Scenario(string id)
        {
            foreach (var s in scenarios)
                if (s.id == id) return s;
            throw new InvalidOperationException($"scenario_spec.json has no scenario '{id}'.");
        }

        // Union of the per-scenario fields read so far; each scenario fills only its own.
        [Serializable]
        internal sealed class ScenarioSpec
        {
            public string id;
            public float lengthMetres;
            public float limitKmh;
            public float stopLine, intersectionCentre, enteredAfter, stopLookWithinMetres, cueAt, hitCheckUntil, doneAt;
            public Range validStop;
            public TooFarSpec stopTooFar;
            public HintCarSpec hintCar;
            public CrossTrafficSpec crossTraffic;
            public ConflictSpec conflict;

            // hidden_ped
            public VariantsSpec variants;
            public float stepOutPoint, startX, endX, stepOutWhenWithinMetres, inRoadBelowX, clearAtX, hitCheckBefore;
            public PeekSpec peek;

            // school, playground
            public ZoneSpec zone;
            public float endAt;
            public PeopleSpec sidewalkPeople;

            // playground (child also used by school_bus later)
            public float crossingPoint, triggerWithinMetres;
            public BallSpec ball;
            public ChildSpec child;

            // lead_car
            public LeadCarSpec leadCar;
            public BrakeSpec brake;
            public HeadwaySpec headway;
            public float removeWhenGapOver, reached80AtKmh;

            // flashing_red (flashPeriodSeconds also ambulance)
            public float flashPeriodSeconds;

            // ambulance
            public float spawnAt, spawnBehind, passedSpeed, passedAheadMetres, goesAroundWithinMetres, goesAroundX, mirrorShowsWithinMetres;
            public ClosingSpec closingSpeed;
            public PullOverSpec pullOver;

            // school_bus
            public BusSpec bus;
            public float noPassPoint, lightsOffAfterClearSeconds;
        }

        [Serializable]
        internal sealed class BusSpec
        {
            public float stopFrontAt, startAheadOfStop, startSpeed, stopSeconds, leaveDelaySeconds, leaveAccel, leaveMaxSpeed, lightFlashPeriodSeconds;
        }

        [Serializable] internal sealed class ClosingSpec { public float abovePlayer, min; }
        [Serializable] internal sealed class PullOverSpec { public float from, to, carX, requiredX, returnX; }

        [Serializable]
        internal sealed class LeadCarSpec
        {
            public float startAhead, startKmh, cruiseTargetGap, cruiseGain, floorGapStart, floorGain, maxKmh;
            public Range floorKmh;
        }

        [Serializable]
        internal sealed class BrakeSpec
        {
            public float atU, orGapAtMost, dropKmh, decel, holdSeconds, waitSeconds, accel;
            public Range targetKmh;
        }

        [Serializable] internal sealed class HeadwaySpec { public float minSeconds, graceSeconds, minPlayerSpeed; }

        [Serializable] internal sealed class BallSpec { public float startX, speed, goneAtX; }

        [Serializable]
        internal sealed class ChildSpec
        {
            public float startX, startBehind, startDelaySeconds, runToRoadSpeed, roadEdgeX, crossSpeed, clearAtX, goneAtX;
            public float atBusSeconds, aheadOfBus, speed; // school_bus
        }

        [Serializable] internal sealed class ZoneSpec { public float from, to, limitKmh, graceSeconds; }
        [Serializable] internal sealed class PeopleSpec { public int count; public float firstAt, spacing, from, to; }

        [Serializable] internal sealed class VariantSpec { public float chance, speed; }
        [Serializable] internal sealed class VariantsSpec { public VariantSpec pedestrian, dog; }
        [Serializable] internal sealed class PeekSpec { public float from, periodSeconds, outSeconds, x; }

        [Serializable] internal sealed class Range { public float from, to; }
        [Serializable] internal sealed class TooFarSpec { public float beforeLineMetres, stoppedSeconds; }
        [Serializable] internal sealed class HintCarSpec { public float at, startDistance, speed; }

        [Serializable]
        internal sealed class CrossTrafficSpec
        {
            public float[] countWeights;
            public Range firstCarDistance, laterCarDelay, lateCarDelay;
            public float laterCarDistance, lastCarLateChance, speed, laneOffset;
        }

        [Serializable]
        internal sealed class ConflictSpec
        {
            public Range pathX;
            public float marginSeconds, minClearSpeed;
        }

        [Serializable] internal sealed class SpeedLimitSpec { public float toleranceKmh, graceSeconds; }
        [Serializable] internal sealed class NudgeSpec { public float stoppedSeconds, quietSeconds, repeatSeconds; }

        [Serializable]
        internal sealed class ModesSpec
        {
            public PracticeSpec practice;
        }

        [Serializable]
        internal sealed class PracticeSpec
        {
            public PauseSpec pauseAndExplain;
        }

        [Serializable]
        internal sealed class PauseSpec
        {
            public string[] pauseKinds;
            public string[] noPauseLines;
            public float alertDelaySeconds;
        }

        [Serializable]
        internal sealed class EngineSpec
        {
            public int[] gearsKmh;
            public float slowerStepSeconds;
            public float fasterStepSeconds;
            public float minChangeRateKmhPerSecond;
            public RearEndSpec rearEnd;
            public LookSpec look;
            public SpeedLimitSpec speedLimit;
            public NudgeSpec nudgeGo;
            public float carLengthMetres;
            public float pullOverLateralRate;
            public float voiceDelaySeconds;
            public float captionSeconds;
            public float failCaptionSeconds;
        }

        [Serializable]
        internal sealed class RearEndSpec
        {
            public float toleranceKmh;
        }

        [Serializable]
        internal sealed class LookSpec
        {
            public float maxRadians;
            public float swipeGain;
            public float lookedSideDegreesWhileStopped;
        }

        static PortSpec current;

        internal static PortSpec Current => current ??= Load();

        static PortSpec Load()
        {
            const string path = "Data/scenario_spec";
            var asset = Resources.Load<TextAsset>(path);
            if (!asset) throw new InvalidOperationException($"Missing Resources/{path}.json");
            var spec = JsonUtility.FromJson<PortSpec>(asset.text);
            if (spec?.engine?.gearsKmh == null || spec.engine.gearsKmh.Length < 2 || spec.engine.rearEnd == null || spec.engine.look == null ||
                spec.engine.speedLimit == null || spec.engine.nudgeGo == null || spec.scenarios == null ||
                spec.modes?.practice?.pauseAndExplain?.pauseKinds == null || spec.modes.practice.pauseAndExplain.noPauseLines == null)
                throw new InvalidOperationException($"Resources/{path}.json is missing engine or practice settings.");
            return spec;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => current = null; // re-read edits on each Play, even with domain reload off
    }
}
