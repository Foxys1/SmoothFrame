using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmoothFrame.Core.Models;

namespace SmoothFrame.Core.Services;

/// <summary>
/// Asynchronous UDP loopback telemetry receiver for Minecraft in-game performance metrics.
/// Listens on 127.0.0.1:28511 for JSON datagrams broadcasted by the SmoothFrame NeoForge mod.
/// Incorporates a 3.0-second heartbeat watchdog timer to detect game session lifecycle state.
/// </summary>
public sealed class MinecraftTelemetryReceiver : IDisposable
{
    public const int DefaultPort = 28511;
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(3.0);

    private readonly object _syncRoot = new();
    private readonly int _port;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private System.Threading.Timer? _watchdogTimer;
    private bool _isDisposed;

    /// <summary>
    /// Raised whenever a valid telemetry packet is received from the game mod.
    /// </summary>
    public event Action<MinecraftTelemetryData>? OnTelemetryReceived;

    /// <summary>
    /// Raised when connection state transitions between active (packets arriving) and inactive (watchdog timeout or shutdown).
    /// </summary>
    public event Action<bool>? OnConnectionStateChanged;

    /// <summary>
    /// Alias event for TelemetryReceived adhering to standard .NET naming conventions.
    /// </summary>
    public event Action<MinecraftTelemetryData>? TelemetryReceived
    {
        add => OnTelemetryReceived += value;
        remove => OnTelemetryReceived -= value;
    }

    /// <summary>
    /// Alias event for ConnectionStateChanged adhering to standard .NET naming conventions.
    /// </summary>
    public event Action<bool>? ConnectionStateChanged
    {
        add => OnConnectionStateChanged += value;
        remove => OnConnectionStateChanged -= value;
    }

    /// <summary>
    /// Bound UDP port number.
    /// </summary>
    public int Port => _port;

    /// <summary>
    /// Indicates whether the listener socket is actively listening.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Indicates whether the game mod is actively transmitting telemetry (heartbeat valid within 3.0s).
    /// </summary>
    public bool IsGameActive { get; private set; }

    /// <summary>
    /// Most recently received in-game performance telemetry datagram.
    /// </summary>
    public MinecraftTelemetryData? LastTelemetryData { get; private set; }

    /// <summary>
    /// Alias for LastTelemetryData.
    /// </summary>
    public MinecraftTelemetryData? LastData => LastTelemetryData;

    /// <summary>
    /// Timestamp of the last received UDP packet.
    /// </summary>
    public DateTimeOffset? LastPacketReceivedTime { get; private set; }

    /// <summary>
    /// Alias for LastPacketReceivedTime.
    /// </summary>
    public DateTimeOffset? LastPacketTime => LastPacketReceivedTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="MinecraftTelemetryReceiver"/> on the specified port.
    /// </summary>
    /// <param name="port">UDP loopback port, default 28511.</param>
    /// <param name="autoStart">If true, starts listening immediately.</param>
    public MinecraftTelemetryReceiver(int port = DefaultPort, bool autoStart = false)
    {
        _port = port > 0 ? port : DefaultPort;
        if (autoStart)
        {
            Start();
        }
    }

    /// <summary>
    /// Binds the UDP loopback socket and initiates background packet processing and watchdog timer.
    /// </summary>
    public void Start()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(MinecraftTelemetryReceiver));
            }

            if (IsRunning)
            {
                return;
            }

            try
            {
                _cts = new CancellationTokenSource();

                var client = new UdpClient();
                client.ExclusiveAddressUse = true;
                client.Client.Bind(new IPEndPoint(IPAddress.Loopback, _port));
                _udpClient = client;

                IsRunning = true;
                _listenTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));

                // 500 ms periodic watchdog check for 3.0s heartbeat expiration
                _watchdogTimer = new System.Threading.Timer(WatchdogTick, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
            }
            catch (Exception ex)
            {
                Stop();
                throw new InvalidOperationException($"Failed to bind UDP telemetry receiver on 127.0.0.1:{_port}: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Stops the socket listener and halts the watchdog timer.
    /// </summary>
    public void Stop()
    {
        bool wasGameActive = false;
        CancellationTokenSource? ctsToCancel = null;
        UdpClient? clientToClose = null;
        System.Threading.Timer? timerToDispose = null;

        lock (_syncRoot)
        {
            if (!IsRunning && _udpClient == null)
            {
                return;
            }

            wasGameActive = IsGameActive;
            IsRunning = false;
            IsGameActive = false;

            ctsToCancel = _cts;
            _cts = null;

            clientToClose = _udpClient;
            _udpClient = null;

            timerToDispose = _watchdogTimer;
            _watchdogTimer = null;
        }

        try
        {
            ctsToCancel?.Cancel();
        }
        catch
        {
            // Ignore cancellation disposal race
        }

        try
        {
            clientToClose?.Close();
            clientToClose?.Dispose();
        }
        catch
        {
            // Ignore socket shutdown exceptions
        }

        try
        {
            timerToDispose?.Dispose();
        }
        catch
        {
            // Ignore timer disposal exceptions
        }

        ctsToCancel?.Dispose();

        if (wasGameActive)
        {
            try
            {
                OnConnectionStateChanged?.Invoke(false);
            }
            catch
            {
                // Never leak event subscriber exceptions
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpClient? client;
            lock (_syncRoot)
            {
                client = _udpClient;
            }

            if (client == null)
            {
                break;
            }

            try
            {
                var result = await client.ReceiveAsync(token).ConfigureAwait(false);
                if (result.Buffer == null || result.Buffer.Length == 0)
                {
                    continue;
                }

                string json = Encoding.UTF8.GetString(result.Buffer);
                var telemetry = JsonSerializer.Deserialize<MinecraftTelemetryData>(json);
                if (telemetry == null)
                {
                    continue;
                }

                bool stateChanged = false;
                lock (_syncRoot)
                {
                    LastTelemetryData = telemetry;
                    LastPacketReceivedTime = DateTimeOffset.UtcNow;

                    if (!IsGameActive)
                    {
                        IsGameActive = true;
                        stateChanged = true;
                    }
                }

                if (stateChanged)
                {
                    try
                    {
                        OnConnectionStateChanged?.Invoke(true);
                    }
                    catch
                    {
                        // Protect receive loop from subscriber faults
                    }
                }

                try
                {
                    OnTelemetryReceived?.Invoke(telemetry);
                }
                catch
                {
                    // Protect receive loop from subscriber faults
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted ||
                                            ex.SocketErrorCode == SocketError.OperationAborted ||
                                            ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
            }
            catch
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private void WatchdogTick(object? state)
    {
        if (!IsRunning)
        {
            return;
        }

        bool timeoutTriggered = false;
        lock (_syncRoot)
        {
            if (IsGameActive && LastPacketReceivedTime.HasValue)
            {
                var elapsed = DateTimeOffset.UtcNow - LastPacketReceivedTime.Value;
                if (elapsed > HeartbeatTimeout)
                {
                    IsGameActive = false;
                    timeoutTriggered = true;
                }
            }
        }

        if (timeoutTriggered)
        {
            try
            {
                OnConnectionStateChanged?.Invoke(false);
            }
            catch
            {
                // Protect watchdog timer from subscriber faults
            }
        }
    }

    /// <summary>
    /// Thread-safe disposal closing socket and watchdog timer cleanly.
    /// </summary>
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
        }

        Stop();
        GC.SuppressFinalize(this);
    }
}
