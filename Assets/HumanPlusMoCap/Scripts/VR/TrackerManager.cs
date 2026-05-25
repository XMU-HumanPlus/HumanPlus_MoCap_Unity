using System.Collections.Generic;
using Messages;
using UnityEngine;

namespace HumanPlusMoCap.Scripts.VR
{
    public class TrackerManager : MonoBehaviour
    {
        [Header("Avatar Source")]
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private bool autoDetectAnimator = true;
        [SerializeField] private bool autoMapOnAwake = true;

        [Header("Trackers")]
        [SerializeField] private List<Tracker> trackers = new List<Tracker>();

        public IReadOnlyList<Tracker> Trackers => trackers;
        public Vector3 HmdPosition { get; private set; }
        public Quaternion HmdRotation { get; private set; } = Quaternion.identity;
        public double HmdTimestamp { get; private set; }
        public float HmdBatteryLevel { get; private set; } = 100f;
        public bool HmdIsCharging { get; private set; }
        public TrackerStatus.Types.Status HmdStatus { get; private set; } = TrackerStatus.Types.Status.Disconnected;
        public bool HmdAdded { get; private set; }
        public bool HasHmdReference { get; private set; }

        private Vector3 _hmdReferencePosition;

        private void Awake()
        {
            EnsureAnimator();

            if (autoMapOnAwake && (trackers == null || trackers.Count == 0))
            {
                AutoMapDefaultTrackers();
            }
        }

        [ContextMenu("Auto Map Default Trackers")]
        public void AutoMapDefaultTrackers()
        {
            EnsureAnimator();
            if (targetAnimator == null)
            {
                Debug.LogError("[TrackerManager] Animator not found. Please assign targetAnimator.");
                return;
            }

            var mapped = new List<Tracker>();
            AddIfBoneExists(mapped, 1, "TRACKER_WAIST", "Waist", TrackerRole.Waist, HumanBodyBones.Hips);
            AddIfBoneExists(mapped, 2, "TRACKER_CHEST", "Chest", TrackerRole.Chest, HumanBodyBones.UpperChest);
            AddIfBoneExists(mapped, 3, "TRACKER_LEFT_FOOT", "LeftFoot", TrackerRole.LeftFoot, HumanBodyBones.LeftFoot);
            AddIfBoneExists(mapped, 4, "TRACKER_RIGHT_FOOT", "RightFoot", TrackerRole.RightFoot, HumanBodyBones.RightFoot);
            AddIfBoneExists(mapped, 5, "TRACKER_LEFT_KNEE", "LeftKnee", TrackerRole.LeftKnee, HumanBodyBones.LeftLowerLeg);
            AddIfBoneExists(mapped, 6, "TRACKER_RIGHT_KNEE", "RightKnee", TrackerRole.RightKnee, HumanBodyBones.RightLowerLeg);
            AddIfBoneExists(mapped, 7, "TRACKER_LEFT_ELBOW", "LeftElbow", TrackerRole.LeftElbow, HumanBodyBones.LeftLowerArm);
            AddIfBoneExists(mapped, 8, "TRACKER_RIGHT_ELBOW", "RightElbow", TrackerRole.RightElbow, HumanBodyBones.RightLowerArm);

            trackers = mapped;
            Debug.Log("[TrackerManager] Auto mapping completed. Tracker count: " + trackers.Count);
        }

        public bool RegisterTrackers(Bridge bridge)
        {
            if (bridge == null)
            {
                return false;
            }

            bool allOk = true;
            if (trackers == null)
            {
                return false;
            }

            for (int i = 0; i < trackers.Count; i++)
            {
                Tracker tracker = trackers[i];
                if (tracker == null || !tracker.Enabled)
                {
                    continue;
                }

                allOk &= bridge.SendTrackerAdded(tracker);
                allOk &= bridge.SendTrackerStatus(tracker.Id, TrackerStatus.Types.Status.Ok);
            }

            return allOk;
        }

        public void SyncHmdState(Bridge bridge)
        {
            if (bridge == null)
            {
                return;
            }

            if (bridge.TryGetHmdPose(out Vector3 position, out Quaternion rotation, out double timestamp))
            {
                HmdPosition = position;
                HmdRotation = rotation;
                HmdTimestamp = timestamp;
            }

            if (bridge.TryGetHmdBattery(out float batteryLevel, out bool isCharging))
            {
                HmdBatteryLevel = batteryLevel;
                HmdIsCharging = isCharging;
            }

            if (bridge.TryGetHmdStatus(out TrackerStatus.Types.Status status))
            {
                HmdStatus = status;
            }

            HmdAdded = bridge.HmdAdded;
        }

        public bool TryCaptureHmdReference()
        {
            if (HmdTimestamp <= 0d)
            {
                return false;
            }

            _hmdReferencePosition = HmdPosition;
            HasHmdReference = true;
            return true;
        }

        public void ResetHmdReference()
        {
            _hmdReferencePosition = Vector3.zero;
            HasHmdReference = false;
        }

        public void SendTrackerFrame(Bridge bridge, bool sendPosition, bool sendRotation)
        {
            if (bridge == null || trackers == null)
            {
                return;
            }

            Vector3 globalOffset = GetGlobalOffset();

            for (int i = 0; i < trackers.Count; i++)
            {
                Tracker tracker = trackers[i];
                if (tracker == null || !tracker.Enabled)
                {
                    continue;
                }

                if (!tracker.TryGetPose(out Vector3 position, out Quaternion rotation))
                {
                    continue;
                }

                Vector3? positionToSend = sendPosition ? (Vector3?)(position + globalOffset) : null;
                Quaternion rotationToSend = sendRotation ? rotation : Quaternion.identity;

                bridge.SendPosition(
                    tracker.Id,
                    positionToSend,
                    rotationToSend,
                    Position.Types.DataSource.Precision);
            }
        }

        private Vector3 GetGlobalOffset()
        {
            if (!HasHmdReference || HmdTimestamp <= 0d)
            {
                return Vector3.zero;
            }

            return HmdPosition - _hmdReferencePosition;
        }

        private void AddIfBoneExists(
            List<Tracker> list,
            int id,
            string serial,
            string name,
            TrackerRole role,
            HumanBodyBones bone)
        {
            Transform transform = targetAnimator.GetBoneTransform(bone);
            if (transform == null)
            {
                Debug.LogWarning("[TrackerManager] Bone not found, skipped: " + bone);
                return;
            }

            list.Add(new Tracker(id, serial, name, role, transform));
        }

        private void EnsureAnimator()
        {
            if (targetAnimator != null)
            {
                return;
            }

            if (autoDetectAnimator)
            {
                targetAnimator = GetComponent<Animator>();
                if (targetAnimator == null)
                {
                    targetAnimator = GetComponentInChildren<Animator>();
                }
            }
        }
    }
}
