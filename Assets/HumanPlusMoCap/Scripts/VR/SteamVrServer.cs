using UnityEngine;

namespace HumanPlusMoCap.Scripts.VR
{
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

        public bool IsRunning => _bridge != null && _bridge.IsRunning;
        public bool IsConnected => _bridge != null && _bridge.IsConnected;
        public bool IsTrackersRegistered => _trackersRegistered;

        private void OnEnable()
        {
            if (autoStartOnEnable)
            {
                StartServer();
            }
        }

        private void OnDisable()
        {
            StopServer();
        }

        private void OnDestroy()
        {
            StopServer();
        }

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

        [ContextMenu("Restart Server")]
        public void RestartServer()
        {
            StopServer();
            StartServer();
        }

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

        public void SetSendEnabled(bool value)
        {
            sendEnabled = value;
        }

        public void SetSendRateHz(float value)
        {
            sendRateHz = Mathf.Max(1f, value);
        }

        public void SetSendPosition(bool value)
        {
            sendPosition = value;
        }

        public void SetSendRotation(bool value)
        {
            sendRotation = value;
        }

        public void SetAutoRegisterTrackersOnConnect(bool value)
        {
            autoRegisterTrackersOnConnect = value;
        }

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

        [ContextMenu("Reset HMD Reference")]
        public void ResetHmdReference()
        {
            EnsureTrackerManager();
            if (trackerManager == null)
            {
                return;
            }

            trackerManager.ResetHmdReference();
        }

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
