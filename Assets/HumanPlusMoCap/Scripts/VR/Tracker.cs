using System;
using UnityEngine;

namespace HumanPlusMoCap.Scripts.VR
{
    /// <summary>
    /// 追踪器角色枚举（与驱动端约定的角色 ID 对应）。
    /// </summary>
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

    /// <summary>
    /// 单个追踪器配置与姿态计算。
    /// </summary>
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

        /// <summary>
        /// 追踪器 ID。
        /// </summary>
        public int Id => id;

        /// <summary>
        /// 追踪器序列号。
        /// </summary>
        public string Serial => serial;

        /// <summary>
        /// 追踪器名称。
        /// </summary>
        public string TrackerName => trackerName;

        /// <summary>
        /// 追踪器角色。
        /// </summary>
        public TrackerRole Role => role;

        /// <summary>
        /// 追踪目标 Transform。
        /// </summary>
        public Transform Target => target;

        /// <summary>
        /// 是否启用该追踪器。
        /// </summary>
        public bool Enabled => enabled;

        /// <summary>
        /// 创建追踪器实例。
        /// </summary>
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

        /// <summary>
        /// 获取追踪器姿态并转换到 SteamVR 右手系。
        /// </summary>
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

        /// <summary>
        /// Unity 左手系到 SteamVR 右手系转换。
        /// </summary>
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
