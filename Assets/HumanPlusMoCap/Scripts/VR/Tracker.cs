using System;
using UnityEngine;

namespace HumanPlusMoCap.Scripts.VR
{
    public enum TrackerRole
    {
        None = 0,
        Waist = 1,
        LeftFoot = 2,
        RightFoot = 3,
        Chest = 4,
        LeftKnee = 5,
        RightKnee = 6,
        LeftElbow = 7,
        RightElbow = 8,
        LeftShoulder = 9,
        RightShoulder = 10,
        LeftHand = 11,
        RightHand = 12,
        Head = 15
    }

    [Serializable]
    public class Tracker
    {
        [SerializeField] private int id;
        [SerializeField] private string serial;
        [SerializeField] private string trackerName;
        [SerializeField] private TrackerRole role;
        [SerializeField] private Transform target;
        [SerializeField] private bool enabled;
        [SerializeField] private Vector3 positionOffset;
        [SerializeField] private Vector3 rotationOffsetEuler;

        public int Id => id;
        public string Serial => serial;
        public string TrackerName => trackerName;
        public TrackerRole Role => role;
        public Transform Target => target;
        public bool Enabled => enabled;

        public Tracker(int id, string serial, string trackerName, TrackerRole role, Transform target)
        {
            this.id = id;
            this.serial = serial;
            this.trackerName = trackerName;
            this.role = role;
            this.target = target;
            enabled = true;
            positionOffset = Vector3.zero;
            rotationOffsetEuler = Vector3.zero;
        }

        public bool TryGetPose(out Vector3 position, out Quaternion rotation)
        {
            if (!enabled || target == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }

            position = target.position + positionOffset;
            rotation = target.rotation * Quaternion.Euler(rotationOffsetEuler);
            return ToSteamVrRightHanded(ref position, ref rotation);
        }

        /// Unity 左手系到 SteamVR 右手系转换。
        private static bool ToSteamVrRightHanded(ref Vector3 position, ref Quaternion rotation)
        {
            if (position == null || rotation == null)
            {
                return false;
            }
            position = new Vector3(position.x, position.y, -position.z);
            rotation = new Quaternion(rotation.x, rotation.y, -rotation.z, -rotation.w);

            return true;
        }
    }
}
