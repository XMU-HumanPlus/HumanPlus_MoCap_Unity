using UnityEngine;

namespace HumanPlusMoCap.Scripts.VR
{
    /// <summary>
    /// SteamVR/SlimeVR 发送端服务，负责连接驱动并推送追踪器数据。
    /// </summary>
    public class SteamVrServer : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string pipeName = @"\\.\pipe\SlimeVRDriver";
        [SerializeField] private bool autoStartOnEnable = true;
        [SerializeField] private bool autoRegisterTrackersOnConnect = true;
        [SerializeField] private bool verboseLogs = false;

        [Header("Sending")]
        [SerializeField] private bool sendEnabled = true;
        [SerializeField] private float sendRateHz = 60f;
        [SerializeField] private bool sendPosition = true;
        [SerializeField] private bool sendRotation = true;
        [SerializeField] private bool useUnscaledTime = false;

        [Header("References")]
        [SerializeField] private TrackerManager trackerManager;

        private Bridge _bridge;
        private bool _trackersRegistered;
        private float _nextSendTime;

        /// <summary>
        /// 服务器是否正在运行。
        /// </summary>
        public bool IsRunning => _bridge != null && _bridge.IsRunning;

        /// <summary>
        /// 是否已连接到驱动。
        /// </summary>
        public bool IsConnected => _bridge != null && _bridge.IsConnected;

        /// <summary>
        /// 是否已完成追踪器注册。
        /// </summary>
        public bool IsTrackersRegistered => _trackersRegistered;

        /// <summary>
        /// 启用时按需启动服务。
        /// </summary>
        private void OnEnable()
        {
            if (autoStartOnEnable)
            {
                StartServer();
            }
        }

        /// <summary>
        /// 禁用时停止服务。
        /// </summary>
        private void OnDisable()
        {
            StopServer();
        }

        /// <summary>
        /// 销毁时确保停止服务。
        /// </summary>
        private void OnDestroy()
        {
            StopServer();
        }

        /// <summary>
        /// 驱动状态同步与追踪器数据发送。
        /// </summary>
        private void Update()
        {
            if (_bridge == null)
            {
                return;
            }

            EnsureTrackerManager();
            if (trackerManager != null)
            {
                trackerManager.SyncHmdState(_bridge);
                trackerManager.TryCaptureHmdReference();
            }

            if (autoRegisterTrackersOnConnect && IsConnected && !_trackersRegistered)
            {
                TryRegisterTrackers();
            }

            if (!sendEnabled || !IsConnected || !_trackersRegistered)
            {
                return;
            }

            if (!IsDue())
            {
                return;
            }

            EnsureTrackerManager();
            if (trackerManager == null)
            {
                return;
            }

            trackerManager.SendTrackerFrame(_bridge, sendPosition, sendRotation);
        }

        /// <summary>
        /// 启动命名管道服务。
        /// </summary>
        [ContextMenu("Start Server")]
        public void StartServer()
        {
            if (_bridge != null && _bridge.IsRunning)
            {
                return;
            }

            EnsureTrackerManager();
            _bridge = new Bridge(pipeName, verboseLogs);
            _bridge.ConnectionStateChanged += OnConnectionStateChanged;
            _bridge.Start();
            trackerManager?.SyncHmdState(_bridge);
            trackerManager?.TryCaptureHmdReference();
            _trackersRegistered = false;
            _nextSendTime = 0f;
        }

        /// <summary>
        /// 停止并释放管道服务。
        /// </summary>
        [ContextMenu("Stop Server")]
        public void StopServer()
        {
            if (_bridge == null)
            {
                return;
            }

            _bridge.ConnectionStateChanged -= OnConnectionStateChanged;
            _bridge.Stop();
            _bridge.Dispose();
            _bridge = null;
            _trackersRegistered = false;
        }

        /// <summary>
        /// 重启命名管道服务。
        /// </summary>
        [ContextMenu("Restart Server")]
        public void RestartServer()
        {
            StopServer();
            StartServer();
        }

        /// <summary>
        /// 向驱动注册追踪器。
        /// </summary>
        [ContextMenu("Register Trackers Now")]
        public bool TryRegisterTrackers()
        {
            if (_bridge == null || !_bridge.IsConnected)
            {
                return false;
            }

            EnsureTrackerManager();
            if (trackerManager == null)
            {
                Debug.LogWarning("[SteamVrServer] TrackerManager reference is missing.");
                return false;
            }

            _trackersRegistered = trackerManager.RegisterTrackers(_bridge);
            return _trackersRegistered;
        }

        /// <summary>
        /// 开关发送功能。
        /// </summary>
        public void SetSendEnabled(bool value)
        {
            sendEnabled = value;
        }

        /// <summary>
        /// 设置发送频率（Hz）。
        /// </summary>
        public void SetSendRateHz(float value)
        {
            sendRateHz = Mathf.Max(1f, value);
        }

        /// <summary>
        /// 设置是否发送位置。
        /// </summary>
        public void SetSendPosition(bool value)
        {
            sendPosition = value;
        }

        /// <summary>
        /// 设置是否发送旋转。
        /// </summary>
        public void SetSendRotation(bool value)
        {
            sendRotation = value;
        }

        /// <summary>
        /// 设置连接后自动注册追踪器。
        /// </summary>
        public void SetAutoRegisterTrackersOnConnect(bool value)
        {
            autoRegisterTrackersOnConnect = value;
        }

        /// <summary>
        /// 重新从 Animator 映射追踪器。
        /// </summary>
        public void RemapTrackersFromAnimator()
        {
            EnsureTrackerManager();
            if (trackerManager == null)
            {
                return;
            }

            trackerManager.AutoMapDefaultTrackers();
            _trackersRegistered = false;
        }

        /// <summary>
        /// 连接状态变更回调。
        /// </summary>
        private void OnConnectionStateChanged(bool connected)
        {
            if (!connected)
            {
                _trackersRegistered = false;
                return;
            }

            if (autoRegisterTrackersOnConnect)
            {
                TryRegisterTrackers();
            }
        }

        /// <summary>
        /// 判断是否达到发送节流时间。
        /// </summary>
        private bool IsDue()
        {
            if (sendRateHz <= 0f)
            {
                return true;
            }

            float now = useUnscaledTime ? Time.unscaledTime : Time.time;
            if (now < _nextSendTime)
            {
                return false;
            }

            _nextSendTime = now + (1f / sendRateHz);
            return true;
        }

        /// <summary>
        /// 确保 TrackerManager 引用可用。
        /// </summary>
        private void EnsureTrackerManager()
        {
            if (trackerManager != null)
            {
                return;
            }

            trackerManager = FindObjectOfType<TrackerManager>();
        }
    }
}
