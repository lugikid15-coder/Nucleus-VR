using System;
using NucleusVR.Maps;
using NucleusVR.Movement;
using NucleusVR.Props;
using NucleusVR.VR;
using UnityEngine;

namespace NucleusVR.Core
{
    internal sealed class CalibrationInteraction : MonoBehaviour
    {
        private static readonly Vector3 PadReach = new(0.29f, 0.24f, 0.22f);
        private const float HoldDuration = 0.22f;

        private CalibrationCourse course;
        private CalibrationRun run;
        private HandLocomotion locomotion;
        private TrackedRig tracking;
        private Transform left;
        private Transform right;
        private DesktopRigDriver desktop;
        private EquipmentHands hands;
        private int previousPad = -1;
        private float heldFor;
        private bool latched;

        public bool SmoothTurn { get; private set; }
        public string Status { get; private set; } = "Touch START to begin";

        public void Configure(CalibrationCourse map, HandLocomotion motor, TrackedRig rig, Transform leftContact,
            Transform rightContact, DesktopRigDriver desktopDriver, EquipmentHands equipmentHands)
        {
            course = map;
            locomotion = motor;
            tracking = rig;
            left = leftContact;
            right = rightContact;
            desktop = desktopDriver;
            hands = equipmentHands;
            run = new CalibrationRun(CalibrationCourse.CheckpointCount);
        }

        public void ResetCourse()
        {
            hands.ReleaseAll();
            locomotion.ResetTo(CalibrationCourse.Spawn, Quaternion.identity);
            course.ResetProps();
            run.Reset();
            course.UpdateSignals(run.NextStation);
            SetStatus();
        }

        public void ToggleTurning()
        {
            SmoothTurn = !SmoothTurn;
            locomotion.SetSmoothTurning(SmoothTurn);
            course.UpdateTurnSignal(SmoothTurn);
        }

        private void LateUpdate()
        {
            if (run == null)
                return;
            if (desktop.ResetPressed)
            {
                ResetCourse();
                return;
            }

            int pad = FindPad();
            if (pad != previousPad)
            {
                previousPad = pad;
                heldFor = 0f;
                latched = false;
            }

            if (pad < 0 || latched)
                return;

            heldFor += Time.unscaledDeltaTime;
            if (heldFor < HoldDuration)
                return;

            latched = true;
            if (pad == course.Stations.Count)
                ResetCourse();
            else if (pad == course.Stations.Count + 1)
                ToggleTurning();
            else if (run.TryActivate(pad, Time.unscaledTimeAsDouble))
            {
                course.UpdateSignals(run.NextStation);
                SetStatus();
            }
        }

        private int FindPad()
        {
            if (Touches(course.ResetPad))
                return course.Stations.Count;
            if (Touches(course.SmoothTurnPad))
                return course.Stations.Count + 1;
            for (int i = 0; i < course.Stations.Count; i++)
                if (Touches(course.Stations[i].transform))
                    return i;
            return -1;
        }

        private bool Touches(Transform target)
        {
            // Physical hands approach from the front. Inactive/stale pose data cannot claim a station.
            return (tracking.LeftHandTracked && AtPad(target.position, left.position)) ||
                   (tracking.RightHandTracked && AtPad(target.position, right.position));
        }

        private static bool AtPad(Vector3 pad, Vector3 hand)
        {
            Vector3 delta = hand - pad;
            return Mathf.Abs(delta.x) <= PadReach.x && Mathf.Abs(delta.y) <= PadReach.y &&
                   delta.z >= -PadReach.z && delta.z <= 0.01f;
        }

        private void SetStatus()
        {
            Status = run.Completed ? FormattableString.Invariant($"Calibration complete · {run.ElapsedSeconds:0.0} s")
                : run.InProgress ? $"Reach checkpoint {run.NextStation} of {run.CheckpointCount}"
                : "Touch START to begin";
        }
    }
}
