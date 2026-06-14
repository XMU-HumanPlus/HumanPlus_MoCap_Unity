using System;
using extOSC;
using UnityEngine;

namespace HumanPlusMoCap.Scripts.VR
{
    /// <summary>
    /// VRChat OSC 发送/接收处理器，负责发送追踪器姿态并接收头部姿态用于对齐。
    /// </summary>
    public class VRCOSCHandler : MonoBehaviour
    {
        /// <summary>
        /// 旋转发送格式。
        /// </summary>
        public enum RotationFormat
        {
            EulerDegrees,
            Quaternion
        }

        /// <summary>
        /// 坐标空间模式。
        /// </summary>
        public enum SpaceMode
        {
            World,
            AvatarLocal
        }

        [Header("OSC Output")]
        [SerializeField] private string remoteIp = "127.0.0.1";
        [SerializeField] private int remotePort = 9000;

        [Header("VRChat Scale")]
        [SerializeField] private float userHeightInMeters = 1.7f;
        [SerializeField] private float senderAvatarHeight = 1.87f;

        [Header("Send Settings")]
        [SerializeField] private bool sendEnabled = true;
        [SerializeField] private float sendRateHz = 60f;
        [SerializeField] private bool useUnscaledTime = false;
        [SerializeField] private RotationFormat rotationFormat = RotationFormat.EulerDegrees;
        [SerializeField] private SpaceMode spaceMode = SpaceMode.World;
        [SerializeField] private bool applyVrcHeadPositionOffset = true;

        [Header("OSC Input (VRChat vrsystem pose only)")]
        [SerializeField] private bool receiveEnabled = true;
        [SerializeField] private int receivePort = 9001;

        [Header("Space Conversion")]
        [SerializeField] private Transform avatarRoot;
        [SerializeField] private Vector3 positionScale = Vector3.one;
        [SerializeField] private Vector3 positionOffset = Vector3.zero;
        [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;

        [Header("Tracking References")]
        [SerializeField] private Transform headReference;
        [SerializeField] private Transform[] trackers = new Transform[8];

        private OSCTransmitter _transmitter;
        private OSCReceiver _receiver;
        private string _lastIp;
        private int _lastPort;
        private float _nextSendTime;
        private int _lastReceivePort = -1;
        private bool _receiverBound;
        private bool _loggedSendError;
        private bool _loggedReceiveError;
        private float _scaleFactor = 1f;

        private bool _hasHeadPose;
        private bool _hasLeftWristPose;
        private bool _hasRightWristPose;
        private Vector3 _receivedHeadPosition;
        private Vector3 _receivedLeftWristPosition;
        private Vector3 _receivedRightWristPosition;
        
        private Vector3 _receivedHeadRotationEuler;
        private Vector3 _receivedLeftWristRotationEuler;
        private Vector3 _receivedRightWristRotationEuler;

        private const string HeadPositionAddress = "/tracking/trackers/head/position";
        private const string HeadRotationAddress = "/tracking/trackers/head/rotation";

        // VRChat vrsystem input endpoints
        private const string VrcHeadPoseAddress = "/tracking/vrsystem/head/pose";
        private const string VrcLeftWristPoseAddress = "/tracking/vrsystem/leftwrist/pose";
        private const string VrcRightWristPoseAddress = "/tracking/vrsystem/rightwrist/pose";

        /// <summary>
        /// 初始化缩放与 OSC 组件。
        /// </summary>
        private void Awake()
        {
            UpdateScaleFactor();
            EnsureTransmitter();
            EnsureReceiver();
        }

        /// <summary>
        /// 启用时确保收发器可用并重置发送节流。
        /// </summary>
        private void OnEnable()
        {
            EnsureTransmitter();
            EnsureReceiver();

            if (_receiver != null)
            {
                _receiver.enabled = true;
            }

            _nextSendTime = 0f;
        }

        /// <summary>
        /// 禁用时释放发送器并关闭接收器。
        /// </summary>
        private void OnDisable()
        {
            DisposeTransmitter();
            if (_receiver != null)
            {
                _receiver.enabled = false;
            }
        }

        /// <summary>
        /// 销毁时清理 OSC 资源。
        /// </summary>
        private void OnDestroy()
        {
            DisposeTransmitter();
            DisposeReceiver();
        }

        /// <summary>
        /// 编辑器校验：保证追踪器数组长度并刷新缩放系数。
        /// </summary>
        private void OnValidate()
        {
            if (trackers == null || trackers.Length != 8)
            {
                Array.Resize(ref trackers, 8);
            }

            UpdateScaleFactor();
        }

        /// <summary>
        /// 依据发送频率按需推送追踪器数据。
        /// </summary>
        private void Update()
        {
            if (!sendEnabled)
            {
                return;
            }

            if (!IsDue(ref _nextSendTime, sendRateHz))
            {
                return;
            }

            SendTrackers();
        }

        /// <summary>
        /// 从 Animator 自动映射 VRChat 追踪器节点。
        /// </summary>
        [ContextMenu("Auto Map Trackers From Animator")]
        private void AutoMapTrackersFromAnimator()
        {
            Animator animator = GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogWarning("[VrChatOscMocapSender] Animator not found on this GameObject.");
                return;
            }

            headReference = animator.GetBoneTransform(HumanBodyBones.Head);

            if (trackers == null || trackers.Length != 8)
            {
                trackers = new Transform[8];
            }

            // VRChat trackers 1..8:
            // 1 Hip, 2 LeftFoot, 3 RightFoot, 4 LeftUpperLeg, 5 RightUpperLeg,
            // 6 UpperChest, 7 LeftUpperArm, 8 RightUpperArm
            trackers[0] = animator.GetBoneTransform(HumanBodyBones.Hips);
            trackers[1] = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            trackers[2] = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            trackers[3] = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            trackers[4] = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            trackers[5] = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            trackers[6] = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            trackers[7] = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        }

        /// <summary>
        /// 发送 VRChat Yaw 校准消息。
        /// </summary>
        [ContextMenu("Send VRChat Yaw Calibration")]
        public void SendVrChatYawCalibration()
        {
            if (headReference == null)
            {
                Debug.LogWarning("[VrChatOscMocapSender] Cannot calibrate yaw without headReference.");
                return;
            }

            EnsureTransmitter();
            if (_transmitter == null)
            {
                return;
            }

            try
            {
                float yawDegrees = GetRotation(headReference).eulerAngles.y;
                var message = new OSCMessage(HeadRotationAddress);
                message.AddValue(OSCValue.Float(0f));
                message.AddValue(OSCValue.Float(-yawDegrees));
                message.AddValue(OSCValue.Float(0f));
                _transmitter.Send(message);
                _loggedSendError = false;
            }
            catch (Exception ex)
            {
                if (!_loggedSendError)
                {
                    _loggedSendError = true;
                    Debug.LogError($"[VrChatOscMocapSender] OSC send failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 发送头部与全身追踪器的姿态数据。
        /// </summary>
        private void SendTrackers()
        {
            EnsureTransmitter();
            if (_transmitter == null)
            {
                return;
            }

            Vector3 globalPositionOffset = GetGlobalPositionOffsetFromVrcHead();

            if (headReference != null)
            {
                Vector3 headPosition = GetPosition(headReference) + globalPositionOffset;
                Quaternion headRotation = GetRotation(headReference);
                SendVector3(HeadPositionAddress, headPosition);
                SendRotation(HeadRotationAddress, headRotation);
            }

            if (trackers == null || trackers.Length == 0)
            {
                return;
            }

            for (int i = 0; i < trackers.Length; i++)
            {
                Transform tracker = trackers[i];
                if (tracker == null)
                {
                    continue;
                }

                string positionAddress = $"/tracking/trackers/{i + 1}/position";
                string rotationAddress = $"/tracking/trackers/{i + 1}/rotation";

                Vector3 position = GetPosition(tracker) + globalPositionOffset;
                Quaternion rotation = GetRotation(tracker);
                SendVector3(positionAddress, position);
                SendRotation(rotationAddress, rotation);
            }
        }

        /// <summary>
        /// 计算基于 VRChat 头部回传的全局位置偏移。
        /// </summary>
        private Vector3 GetGlobalPositionOffsetFromVrcHead()
        {
            if (!applyVrcHeadPositionOffset || !_hasHeadPose || headReference == null)
            {
                return Vector3.zero;
            }

            Vector3 localHeadPosition = GetPosition(headReference);
            return _receivedHeadPosition - localHeadPosition;
        }

        /// <summary>
        /// 根据空间模式与缩放设置获取位置。
        /// </summary>
        private Vector3 GetPosition(Transform target)
        {
            Vector3 position = target.position;

            if (spaceMode == SpaceMode.AvatarLocal && avatarRoot != null)
            {
                position = avatarRoot.InverseTransformPoint(position);
            }

            position *= _scaleFactor;
            position = Vector3.Scale(position, positionScale) + positionOffset;
            return position;
        }

        /// <summary>
        /// 根据空间模式与旋转偏移获取旋转。
        /// </summary>
        private Quaternion GetRotation(Transform target)
        {
            Quaternion rotation = target.rotation;

            if (spaceMode == SpaceMode.AvatarLocal && avatarRoot != null)
            {
                rotation = Quaternion.Inverse(avatarRoot.rotation) * rotation;
            }

            if (rotationOffsetEuler != Vector3.zero)
            {
                rotation = Quaternion.Euler(rotationOffsetEuler) * rotation;
            }

            return rotation;
        }

        /// <summary>
        /// 根据配置选择欧拉角或四元数发送。
        /// </summary>
        private void SendRotation(string address, Quaternion rotation)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            if (rotationFormat == RotationFormat.Quaternion)
            {
                SendQuaternion(address, rotation);
                return;
            }

            SendVector3(address, rotation.eulerAngles);
        }

        /// <summary>
        /// 发送三维向量 OSC 消息。
        /// </summary>
        private void SendVector3(string address, Vector3 value)
        {
            if (string.IsNullOrWhiteSpace(address) || _transmitter == null)
            {
                return;
            }

            try
            {
                var message = new OSCMessage(address);
                message.AddValue(OSCValue.Float(value.x));
                message.AddValue(OSCValue.Float(value.y));
                message.AddValue(OSCValue.Float(value.z));
                _transmitter.Send(message);
                _loggedSendError = false;
            }
            catch (Exception ex)
            {
                if (!_loggedSendError)
                {
                    _loggedSendError = true;
                    Debug.LogError($"[VrChatOscMocapSender] OSC send failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 发送四元数 OSC 消息。
        /// </summary>
        private void SendQuaternion(string address, Quaternion rotation)
        {
            if (string.IsNullOrWhiteSpace(address) || _transmitter == null)
            {
                return;
            }

            try
            {
                var message = new OSCMessage(address);
                message.AddValue(OSCValue.Float(rotation.x));
                message.AddValue(OSCValue.Float(rotation.y));
                message.AddValue(OSCValue.Float(rotation.z));
                message.AddValue(OSCValue.Float(rotation.w));
                _transmitter.Send(message);
                _loggedSendError = false;
            }
            catch (Exception ex)
            {
                if (!_loggedSendError)
                {
                    _loggedSendError = true;
                    Debug.LogError($"[VrChatOscMocapSender] OSC send failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 根据发送者与用户身高计算缩放比例。
        /// </summary>
        private void UpdateScaleFactor()
        {
            if (senderAvatarHeight <= 0f)
            {
                _scaleFactor = 1f;
                return;
            }

            _scaleFactor = userHeightInMeters / senderAvatarHeight;
        }

        /// <summary>
        /// 初始化或更新 OSC 发送器参数。
        /// </summary>
        private void EnsureTransmitter()
        {
            if (!sendEnabled)
            {
                return;
            }

            if (_transmitter == null)
            {
                _transmitter = GetComponent<OSCTransmitter>();
                if (_transmitter == null)
                {
                    _transmitter = gameObject.AddComponent<OSCTransmitter>();
                }
            }

            if (_lastIp == remoteIp && _lastPort == remotePort)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(remoteIp))
            {
                remoteIp = "127.0.0.1";
            }

            _transmitter.RemoteHost = remoteIp;
            _transmitter.RemotePort = remotePort;
            _lastIp = remoteIp;
            _lastPort = remotePort;
        }

        /// <summary>
        /// 初始化或更新 OSC 接收器，并绑定回调。
        /// </summary>
        private void EnsureReceiver()
        {
            if (!receiveEnabled)
            {
                return;
            }

            if (_receiver == null)
            {
                _receiver = GetComponent<OSCReceiver>();
                if (_receiver == null)
                {
                    _receiver = gameObject.AddComponent<OSCReceiver>();
                }
            }

            if (receivePort <= 0)
            {
                receivePort = 9001;
            }

            if (_lastReceivePort != receivePort)
            {
                _receiver.LocalPort = receivePort;
                _lastReceivePort = receivePort;
            }

            if (_receiverBound)
            {
                return;
            }

            _receiver.Bind(VrcHeadPoseAddress, OnHeadPoseReceived);
            _receiver.Bind(VrcLeftWristPoseAddress, OnLeftWristPoseReceived);
            _receiver.Bind(VrcRightWristPoseAddress, OnRightWristPoseReceived);
            _receiverBound = true;
        }

        /// <summary>
        /// 释放接收器状态。
        /// </summary>
        private void DisposeReceiver()
        {
            _receiver = null;
            _receiverBound = false;
        }

        /// <summary>
        /// 接收 VRChat 头部姿态回调。
        /// </summary>
        private void OnHeadPoseReceived(OSCMessage message)
        {
            if (!TryReadPose(message, out Vector3 position, out Vector3 rotationEuler))
            {
                LogReceiveErrorOnce("Head pose message format is invalid.");
                return;
            }

            _loggedReceiveError = false;
            _receivedHeadPosition = position;
            _receivedHeadRotationEuler = rotationEuler;
            _hasHeadPose = true;
        }

        /// <summary>
        /// 接收 VRChat 左手腕姿态回调。
        /// </summary>
        private void OnLeftWristPoseReceived(OSCMessage message)
        {
            if (!TryReadPose(message, out Vector3 position, out Vector3 rotationEuler))
            {
                LogReceiveErrorOnce("Left wrist pose message format is invalid.");
                return;
            }

            _loggedReceiveError = false;
            _receivedLeftWristPosition = position;
            _receivedLeftWristRotationEuler = rotationEuler;
            _hasLeftWristPose = true;
        }

        /// <summary>
        /// 接收 VRChat 右手腕姿态回调。
        /// </summary>
        private void OnRightWristPoseReceived(OSCMessage message)
        {
            if (!TryReadPose(message, out Vector3 position, out Vector3 rotationEuler))
            {
                LogReceiveErrorOnce("Right wrist pose message format is invalid.");
                return;
            }

            _loggedReceiveError = false;
            _receivedRightWristPosition = position;
            _receivedRightWristRotationEuler = rotationEuler;
            _hasRightWristPose = true;
        }

        /// <summary>
        /// 从 OSC 消息解析位置与欧拉角。
        /// </summary>
        private static bool TryReadPose(OSCMessage message, out Vector3 position, out Vector3 rotationEuler)
        {
            position = Vector3.zero;
            rotationEuler = Vector3.zero;
            if (message == null || message.Values == null || message.Values.Count < 6)
            {
                return false;
            }

            if (!TryReadFloat(message.Values[0], out float x) ||
                !TryReadFloat(message.Values[1], out float y) ||
                !TryReadFloat(message.Values[2], out float z) ||
                !TryReadFloat(message.Values[3], out float yaw) ||
                !TryReadFloat(message.Values[4], out float pitch) ||
                !TryReadFloat(message.Values[5], out float roll))
            {
                return false;
            }

            position = new Vector3(x, y, z);
            rotationEuler = new Vector3(yaw, pitch, roll);
            return true;
        }

        /// <summary>
        /// 读取 OSC 数值并转换为 float。
        /// </summary>
        private static bool TryReadFloat(OSCValue oscValue, out float value)
        {
            value = 0f;
            if (oscValue == null)
            {
                return false;
            }

            switch (oscValue.Type)
            {
                case OSCValueType.Float:
                    value = oscValue.FloatValue;
                    return true;
                case OSCValueType.Double:
                    value = (float)oscValue.DoubleValue;
                    return true;
                case OSCValueType.Int:
                    value = oscValue.IntValue;
                    return true;
                case OSCValueType.Long:
                    value = oscValue.LongValue;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 判断是否到达发送时机。
        /// </summary>
        private bool IsDue(ref float nextTime, float rateHz)
        {
            if (rateHz <= 0f)
            {
                return true;
            }

            float now = useUnscaledTime ? Time.unscaledTime : Time.time;
            if (now < nextTime)
            {
                return false;
            }

            nextTime = now + 1f / rateHz;
            return true;
        }

        /// <summary>
        /// 接收错误只记录一次，避免刷屏。
        /// </summary>
        private void LogReceiveErrorOnce(string error)
        {
            if (_loggedReceiveError)
            {
                return;
            }

            _loggedReceiveError = true;
            Debug.LogError($"[VrChatOscMocapSender] OSC receive failed: {error}");
        }

        /// <summary>
        /// 释放发送器状态。
        /// </summary>
        private void DisposeTransmitter()
        {
            _transmitter = null;
        }
    }
}
