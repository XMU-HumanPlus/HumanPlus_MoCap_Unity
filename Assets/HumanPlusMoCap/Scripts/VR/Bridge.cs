using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Google.Protobuf;
using Messages;
using UnityEngine;

namespace HumanPlusMoCap.Scripts.VR
{
    /// <summary>
    /// SlimeVR/SteamVR 驱动的命名管道桥接器，负责读写 protobuf 消息。
    /// </summary>
    public sealed class Bridge : IDisposable
    {
        private readonly string _pipeName;
        private readonly bool _verbose;
        private readonly object _pipeLock = new object();
        private readonly object _writeLock = new object();
        private readonly object _stateLock = new object();

        private Thread _workerThread;
        private volatile bool _running;
        private volatile bool _connected;
        private NamedPipeServerStream _pipe;

        private Vector3 _hmdPosition;
        private Quaternion _hmdRotation = Quaternion.identity;
        private double _hmdTimestamp;
        private float _hmdBatteryLevel = 100f;
        private bool _hmdIsCharging;
        private TrackerStatus.Types.Status _hmdStatus = TrackerStatus.Types.Status.Disconnected;
        private bool _hmdAdded;

        /// <summary>
        /// 连接状态变更事件（true=已连接）。
        /// </summary>
        public event Action<bool> ConnectionStateChanged;

        /// <summary>
        /// 是否已连接到驱动。
        /// </summary>
        public bool IsConnected => _connected;

        /// <summary>
        /// 是否正在运行服务线程。
        /// </summary>
        public bool IsRunning => _running;

        /// <summary>
        /// 驱动端是否已注册 HMD。
        /// </summary>
        public bool HmdAdded
        {
            get
            {
                lock (_stateLock)
                {
                    return _hmdAdded;
                }
            }
        }

        /// <summary>
        /// 创建命名管道桥接器。
        /// </summary>
        public Bridge(string pipeName, bool verbose)
        {
            _pipeName = NormalizePipeName(pipeName);
            _verbose = verbose;
        }

        /// <summary>
        /// 启动管道服务线程。
        /// </summary>
        public void Start()
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _workerThread = new Thread(ServerLoop) { IsBackground = true };
            _workerThread.Start();
        }

        /// <summary>
        /// 停止服务线程并断开连接。
        /// </summary>
        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            DisconnectPipe();
            if (_workerThread != null && _workerThread.IsAlive)
            {
                _workerThread.Join(500);
            }
            _workerThread = null;
            SetConnected(false);
        }

        /// <summary>
        /// 通知驱动新增追踪器。
        /// </summary>
        public bool SendTrackerAdded(Tracker tracker)
        {
            if (tracker == null)
            {
                return false;
            }

            var message = new ProtobufMessage
            {
                TrackerAdded = new TrackerAdded
                {
                    TrackerId = tracker.Id,
                    TrackerSerial = tracker.Serial ?? string.Empty,
                    TrackerName = tracker.TrackerName ?? string.Empty,
                    TrackerRole = (int)tracker.Role
                }
            };

            return SendMessage(message);
        }

        /// <summary>
        /// 发送追踪器状态。
        /// </summary>
        public bool SendTrackerStatus(int trackerId, TrackerStatus.Types.Status status)
        {
            var message = new ProtobufMessage
            {
                TrackerStatus = new TrackerStatus
                {
                    TrackerId = trackerId,
                    Status = status
                }
            };

            return SendMessage(message);
        }

        /// <summary>
        /// 发送追踪器位置与旋转。
        /// </summary>
        public bool SendPosition(
            int trackerId,
            Vector3? position,
            Quaternion rotation,
            Position.Types.DataSource dataSource)
        {
            var positionMessage = new Position
            {
                TrackerId = trackerId,
                Qx = rotation.x,
                Qy = rotation.y,
                Qz = rotation.z,
                Qw = rotation.w,
                DataSource = dataSource
            };

            if (position.HasValue)
            {
                Vector3 pos = position.Value;
                positionMessage.X = pos.x;
                positionMessage.Y = pos.y;
                positionMessage.Z = pos.z;
            }

            var message = new ProtobufMessage
            {
                Position = positionMessage
            };

            return SendMessage(message);
        }

        /// <summary>
        /// 发送追踪器电量信息。
        /// </summary>
        public bool SendBattery(int trackerId, float batteryLevel, bool isCharging)
        {
            var message = new ProtobufMessage
            {
                Battery = new Battery
                {
                    TrackerId = trackerId,
                    BatteryLevel = batteryLevel,
                    IsCharging = isCharging
                }
            };

            return SendMessage(message);
        }

        /// <summary>
        /// 获取 HMD 姿态与时间戳。
        /// </summary>
        public bool TryGetHmdPose(out Vector3 position, out Quaternion rotation, out double timestamp)
        {
            lock (_stateLock)
            {
                position = _hmdPosition;
                rotation = _hmdRotation;
                timestamp = _hmdTimestamp;
            }

            return timestamp > 0d;
        }

        /// <summary>
        /// 获取 HMD 电量信息。
        /// </summary>
        public bool TryGetHmdBattery(out float batteryLevel, out bool isCharging)
        {
            lock (_stateLock)
            {
                batteryLevel = _hmdBatteryLevel;
                isCharging = _hmdIsCharging;
            }

            return true;
        }

        /// <summary>
        /// 获取 HMD 连接状态。
        /// </summary>
        public bool TryGetHmdStatus(out TrackerStatus.Types.Status status)
        {
            lock (_stateLock)
            {
                status = _hmdStatus;
            }

            return true;
        }

        /// <summary>
        /// 序列化并发送 protobuf 消息。
        /// </summary>
        private bool SendMessage(ProtobufMessage message)
        {
            if (message == null)
            {
                return false;
            }

            return SendPayload(message.ToByteArray());
        }

        /// <summary>
        /// 管道主循环：等待连接并读取数据。
        /// </summary>
        private void ServerLoop()
        {
            while (_running)
            {
                try
                {
                    var localPipe = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    lock (_pipeLock)
                    {
                        _pipe = localPipe;
                    }

                    if (_verbose)
                    {
                        Debug.Log("[Bridge] Waiting for driver connection on pipe: " + _pipeName);
                    }

                    localPipe.WaitForConnection();
                    SetConnected(true);

                    if (_verbose)
                    {
                        Debug.Log("[Bridge] Driver connected.");
                    }

                    RunReadLoop(localPipe);
                }
                catch (Exception ex)
                {
                    if (_running && _verbose)
                    {
                        Debug.LogWarning("[Bridge] Pipe loop error: " + ex.Message);
                    }
                }
                finally
                {
                    DisconnectPipe();
                    SetConnected(false);
                }

                if (_running)
                {
                    Thread.Sleep(200);
                }
            }
        }

        /// <summary>
        /// 读取并解析驱动侧的消息流。
        /// </summary>
        private void RunReadLoop(NamedPipeServerStream pipe)
        {
            byte[] sizeBuffer = new byte[4];
            while (_running && pipe != null && pipe.IsConnected)
            {
                if (!TryReadExact(pipe, sizeBuffer, 4))
                {
                    break;
                }

                uint totalSize = BitConverter.ToUInt32(sizeBuffer, 0);
                int payloadLength = (int)totalSize - 4;
                if (payloadLength < 0 || payloadLength > 1024 * 1024)
                {
                    if (_verbose)
                    {
                        Debug.LogWarning("[Bridge] Invalid payload length: " + payloadLength);
                    }
                    break;
                }

                if (payloadLength == 0)
                {
                    continue;
                }

                byte[] payload = new byte[payloadLength];
                if (!TryReadExact(pipe, payload, payloadLength))
                {
                    break;
                }

                try
                {
                    HandleIncomingMessage(ProtobufMessage.Parser.ParseFrom(payload));
                }
                catch (Exception ex)
                {
                    if (_verbose)
                    {
                        Debug.LogWarning("[Bridge] Parse failed: " + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// 从流中读取指定字节数。
        /// </summary>
        private bool TryReadExact(Stream stream, byte[] buffer, int length)
        {
            int offset = 0;
            while (_running && offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read <= 0)
                {
                    return false;
                }
                offset += read;
            }

            return offset == length;
        }

        /// <summary>
        /// 分发来自驱动的消息。
        /// </summary>
        private void HandleIncomingMessage(ProtobufMessage message)
        {
            if (message == null)
            {
                return;
            }

            switch (message.MessageCase)
            {
                case ProtobufMessage.MessageOneofCase.Position:
                    UpdateHmdPose(message.Position);
                    break;
                case ProtobufMessage.MessageOneofCase.Battery:
                    UpdateHmdBattery(message.Battery);
                    break;
                case ProtobufMessage.MessageOneofCase.TrackerStatus:
                    UpdateHmdStatus(message.TrackerStatus);
                    break;
                case ProtobufMessage.MessageOneofCase.TrackerAdded:
                    UpdateHmdAdded(message.TrackerAdded);
                    break;
            }
        }

        /// <summary>
        /// 更新 HMD 姿态缓存。
        /// </summary>
        private void UpdateHmdPose(Position position)
        {
            if (position == null)
            {
                return;
            }

            lock (_stateLock)
            {
                _hmdPosition = new Vector3(position.X, position.Y, position.Z);
                _hmdRotation = new Quaternion(position.Qx, position.Qy, position.Qz, position.Qw);
                _hmdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            }
        }

        /// <summary>
        /// 更新 HMD 电量缓存。
        /// </summary>
        private void UpdateHmdBattery(Battery battery)
        {
            if (battery == null)
            {
                return;
            }

            lock (_stateLock)
            {
                _hmdBatteryLevel = battery.BatteryLevel;
                _hmdIsCharging = battery.IsCharging;
            }
        }

        /// <summary>
        /// 更新 HMD 状态缓存。
        /// </summary>
        private void UpdateHmdStatus(TrackerStatus status)
        {
            if (status == null)
            {
                return;
            }

            lock (_stateLock)
            {
                _hmdStatus = status.Status;
            }
        }

        /// <summary>
        /// 标记 HMD 已被驱动端注册。
        /// </summary>
        private void UpdateHmdAdded(TrackerAdded trackerAdded)
        {
            if (trackerAdded == null)
            {
                return;
            }

            lock (_stateLock)
            {
                _hmdAdded = true;
            }
        }

        /// <summary>
        /// 写入带长度前缀的 payload。
        /// </summary>
        private bool SendPayload(byte[] payload)
        {
            if (!_running || !_connected || payload == null || payload.Length == 0)
            {
                return false;
            }

            NamedPipeServerStream localPipe;
            lock (_pipeLock)
            {
                localPipe = _pipe;
            }

            if (localPipe == null || !localPipe.IsConnected)
            {
                return false;
            }

            try
            {
                int packetLength = payload.Length + 4;
                byte[] packet = new byte[packetLength];
                Buffer.BlockCopy(BitConverter.GetBytes(packetLength), 0, packet, 0, 4);
                Buffer.BlockCopy(payload, 0, packet, 4, payload.Length);

                lock (_writeLock)
                {
                    localPipe.Write(packet, 0, packet.Length);
                    localPipe.Flush();
                }

                return true;
            }
            catch (Exception ex)
            {
                if (_verbose)
                {
                    Debug.LogWarning("[Bridge] Send failed: " + ex.Message);
                }
                DisconnectPipe();
                SetConnected(false);
                return false;
            }
        }

        /// <summary>
        /// 断开并释放当前管道。
        /// </summary>
        private void DisconnectPipe()
        {
            NamedPipeServerStream localPipe;
            lock (_pipeLock)
            {
                localPipe = _pipe;
                _pipe = null;
            }

            if (localPipe != null)
            {
                try
                {
                    if (localPipe.IsConnected)
                    {
                        localPipe.Disconnect();
                    }
                }
                catch
                {
                    // ignored
                }

                try
                {
                    localPipe.Dispose();
                }
                catch
                {
                    // ignored
                }
            }
        }

        /// <summary>
        /// 更新连接状态并触发事件。
        /// </summary>
        private void SetConnected(bool connected)
        {
            if (_connected == connected)
            {
                return;
            }

            _connected = connected;
            ConnectionStateChanged?.Invoke(connected);
        }

        /// <summary>
        /// 规范化管道名称（剥离前缀）。
        /// </summary>
        private static string NormalizePipeName(string pipeName)
        {
            const string prefix = @"\\.\pipe\";

            if (string.IsNullOrWhiteSpace(pipeName))
            {
                return "TrackingDriver";
            }

            if (pipeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return pipeName.Substring(prefix.Length);
            }

            return pipeName;
        }

        /// <summary>
        /// 释放资源并停止服务。
        /// </summary>
        public void Dispose()
        {
            Stop();
        }
    }
}
