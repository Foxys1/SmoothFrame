package com.smoothframe.mod.telemetry;

import com.smoothframe.mod.optimizer.ChunkUploadThrottler;
import com.smoothframe.mod.optimizer.EntityCullingHandler;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;

import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.neoforge.event.GameShuttingDownEvent;

/**
 * Background UDP loopback telemetry broadcaster.
 * Transmits real-time in-game performance metrics to SmoothFrame app on 127.0.0.1:28511.
 */
public class TelemetryServer implements Runnable {
    public static final String TELEMETRY_HOST = "127.0.0.1";
    public static final int TELEMETRY_PORT = 28511;
    public static final int BROADCAST_INTERVAL_MS = 250; // 4 Hz

    private static final TelemetryServer INSTANCE = new TelemetryServer();

    public static TelemetryServer getInstance() {
        return INSTANCE;
    }

    private volatile boolean running = false;
    private Thread workerThread;
    private volatile DatagramSocket socket;

    private TelemetryServer() {
    }

    public synchronized void start() {
        if (running) {
            return;
        }
        running = true;
        try {
            Runtime.getRuntime().addShutdownHook(new Thread(this::stop, "SmoothFrame-TelemetryShutdownHook"));
        } catch (Exception ignored) {
        }
        workerThread = new Thread(this, "SmoothFrame-Telemetry");
        workerThread.setDaemon(true);
        workerThread.start();
    }

    public synchronized void stop() {
        running = false;
        if (socket != null && !socket.isClosed()) {
            try {
                socket.close();
            } catch (Exception ignored) {
            }
        }
        if (workerThread != null) {
            workerThread.interrupt();
            if (Thread.currentThread() != workerThread) {
                try {
                    workerThread.join(1000);
                } catch (InterruptedException ignored) {
                }
            }
            workerThread = null;
        }
    }

    @SubscribeEvent
    public void onGameShuttingDown(GameShuttingDownEvent event) {
        stop();
    }

    @Override
    public void run() {
        InetAddress targetAddress = null;

        while (running) {
            try {
                // Ensure socket is created or recreated if closed/failed
                if (socket == null || socket.isClosed()) {
                    try {
                        socket = new DatagramSocket();
                    } catch (Exception e) {
                        socket = null;
                    }
                }

                // Ensure target host address is resolved
                if (targetAddress == null) {
                    try {
                        targetAddress = InetAddress.getByName(TELEMETRY_HOST);
                    } catch (Exception e) {
                        targetAddress = null;
                    }
                }

                if (socket != null && targetAddress != null) {
                    FpsTracker tracker = FpsTracker.getInstance();
                    EntityCullingHandler culler = EntityCullingHandler.getInstance();
                    ChunkUploadThrottler throttler = ChunkUploadThrottler.getInstance();

                    int fps = tracker.getActiveGameplayFps() > 0 ? tracker.getActiveGameplayFps() : tracker.getCurrentFps();
                    int low1 = tracker.getActiveOnePercentLow() > 0 ? tracker.getActiveOnePercentLow() : tracker.getOnePercentLow();
                    double ftMs = tracker.getActiveGameplayFps() > 0 ? tracker.getActiveFrametimeMs() : tracker.getCurrentFrametimeMs();

                    Runtime rt = Runtime.getRuntime();
                    long usedMb = (rt.totalMemory() - rt.freeMemory()) / (1024L * 1024L);
                    long maxMemoryBytes = rt.maxMemory();
                    long maxMb;
                    if (maxMemoryBytes == Long.MAX_VALUE || maxMemoryBytes <= 0) {
                        maxMb = rt.totalMemory() / (1024L * 1024L);
                    } else {
                        maxMb = maxMemoryBytes / (1024L * 1024L);
                        if (maxMb > 1024L * 1024L) { // clamp if no -Xmx / unbounded (> 1 TB)
                            maxMb = rt.totalMemory() / (1024L * 1024L);
                        }
                    }

                    int pendingChunks = throttler.getPendingUploads();
                    int culledEntities = culler.getAndResetCulledCount();
                    long timestamp = System.currentTimeMillis();

                    // Build JSON payload efficiently using StringBuilder without regex/String.format allocations
                    StringBuilder sb = new StringBuilder(256);
                    sb.append("{\"source\":\"SmoothFrameMod\",\"version\":\"1.0.0\",\"mc_version\":\"1.21.1\",");
                    sb.append("\"fps\":").append(fps).append(',');
                    sb.append("\"one_percent_low\":").append(low1).append(',');
                    sb.append("\"frametime_ms\":");
                    long ftScaled = Math.round(ftMs * 100.0);
                    sb.append(ftScaled / 100).append('.');
                    long dec = Math.abs(ftScaled % 100);
                    if (dec < 10) {
                        sb.append('0');
                    }
                    sb.append(dec).append(',');
                    sb.append("\"memory_used_mb\":").append(usedMb).append(',');
                    sb.append("\"memory_max_mb\":").append(maxMb).append(',');
                    sb.append("\"chunks_pending\":").append(pendingChunks).append(',');
                    sb.append("\"entities_culled\":").append(culledEntities).append(',');
                    sb.append("\"timestamp_ms\":").append(timestamp).append('}');

                    byte[] data = sb.toString().getBytes(StandardCharsets.UTF_8);
                    DatagramPacket packet = new DatagramPacket(data, data.length, targetAddress, TELEMETRY_PORT);
                    socket.send(packet);
                }

                Thread.sleep(BROADCAST_INTERVAL_MS);
            } catch (InterruptedException e) {
                break;
            } catch (Exception e) {
                // MOD-02: Prevent socket thrashing: reuse the open socket; do NOT destroy/recreate every 250ms
                if (socket != null && socket.isClosed()) {
                    socket = null;
                }

                try {
                    Thread.sleep(BROADCAST_INTERVAL_MS);
                } catch (InterruptedException ie) {
                    break;
                }
            }
        }

        if (socket != null && !socket.isClosed()) {
            try {
                socket.close();
            } catch (Exception ignored) {
            }
        }
    }
}
