using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Google.Protobuf;
using Messages;
using UnityEngine;

namespace HumanPlusMoCap.Scripts.VR
{
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

        public event Action<bool> ConnectionStateChanged;

        public bool IsConnected => _connected;
        public bool IsRunning => _running;

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

        public Bridge(string pipeName, bool verbose)
        {
            _pipeName = NormalizePipeName(pipeName);
            _verbose = verbose;
        }

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

        public bool TryGetHmdBattery(out float batteryLevel, out bool isCharging)
        {
            lock (_stateLock)
            {
                batteryLevel = _hmdBatteryLevel;
                isCharging = _hmdIsCharging;
            }

            return true;
        }

        public bool TryGetHmdStatus(out TrackerStatus.Types.Status status)
        {
            lock (_stateLock)
            {
                status = _hmdStatus;
            }

            return true;
        }

        private bool SendMessage(ProtobufMessage message)
        {
            if (message == null)
            {
                return false;
            }

            return SendPayload(message.ToByteArray());
        }

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

        private void SetConnected(bool connected)
        {
            if (_connected == connected)
            {
                return;
            }

            _connected = connected;
            ConnectionStateChanged?.Invoke(connected);
        }

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

        public void Dispose()
        {
            Stop();
        }
    }
}
