package com.smoothframe.mod.telemetry;

import java.util.Arrays;
import net.minecraft.client.Minecraft;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.neoforge.client.event.RenderFrameEvent;

/**
 * High-performance nanosecond frametime tracker and 1% low calculator.
 * Operates with zero heap allocations on the render loop via circular primitive buffers.
 * Accurately tracks active in-game gameplay rendering FPS and preserves metrics during
 * background energy-saving throttling.
 */
public class FpsTracker {
    private static final FpsTracker INSTANCE = new FpsTracker();

    public static FpsTracker getInstance() {
        return INSTANCE;
    }

    private static final int BUFFER_SIZE = 120;
    private final long[] activeFrameTimesNanos = new long[BUFFER_SIZE];
    private final long[] sortBuffer = new long[BUFFER_SIZE];
    private int activeFrameIndex = 0;
    private int activeFrameCount = 0;
    private long activeRunningTotalNanos = 0;
    private int framesSinceSort = 0;

    private long lastFrameNanoTime = 0;
    private boolean wasWindowActive = true;

    // Active gameplay rendering metrics (updated only while window is active)
    private volatile int activeGameplayFps = 0;
    private volatile int activeOnePercentLow = 0;
    private volatile double activeFrametimeMs = 0.0;

    // Current/raw metrics (reflects throttled 15/30 FPS when inactive)
    private volatile int currentFps = 0;
    private volatile int onePercentLow = 0;
    private volatile double currentFrametimeMs = 0.0;
    private volatile long lastFrameDeltaNanos = 0;

    private FpsTracker() {
    }

    @SubscribeEvent
    public void onRenderFramePost(RenderFrameEvent.Post event) {
        long now = System.nanoTime();
        if (lastFrameNanoTime <= 0) {
            lastFrameNanoTime = now;
            return;
        }

        long delta = now - lastFrameNanoTime;
        lastFrameNanoTime = now;

        if (delta <= 0) {
            return;
        }

        // Filter out frametimes > 1.0s resulting from pause screen, dimension loading screens,
        // or window focus transition spikes
        if (delta > 1_000_000_000L) {
            return;
        }

        lastFrameDeltaNanos = delta;
        currentFrametimeMs = delta / 1_000_000.0;

        Minecraft mc = Minecraft.getInstance();
        boolean isActive = mc != null && mc.isWindowActive();

        // Avoid frametime spike when regaining window focus
        if (isActive && !wasWindowActive) {
            wasWindowActive = true;
            return;
        }
        wasWindowActive = isActive;

        if (isActive) {
            activeFrametimeMs = currentFrametimeMs;

            long oldDelta = activeFrameTimesNanos[activeFrameIndex];
            activeFrameTimesNanos[activeFrameIndex] = delta;
            activeFrameIndex = (activeFrameIndex + 1) % BUFFER_SIZE;

            if (activeFrameCount < BUFFER_SIZE) {
                activeFrameCount++;
                activeRunningTotalNanos += delta;
            } else {
                activeRunningTotalNanos += (delta - oldDelta);
            }

            if (activeRunningTotalNanos > 0) {
                activeGameplayFps = (int) Math.round(((double) activeFrameCount * 1_000_000_000.0) / (double) activeRunningTotalNanos);
                currentFps = activeGameplayFps;
            }

            // Periodic 1% low computation (every 20 frames or during warmup) to eliminate per-frame sort on render thread
            framesSinceSort++;
            if (framesSinceSort >= 20 || activeFrameCount < 20) {
                framesSinceSort = 0;
                int count = activeFrameCount;
                System.arraycopy(activeFrameTimesNanos, 0, sortBuffer, 0, count);
                Arrays.sort(sortBuffer, 0, count);
                int p99Index = Math.min(count - 1, (int) Math.floor(count * 0.99));
                long p99Nanos = sortBuffer[p99Index];
                if (p99Nanos > 0) {
                    activeOnePercentLow = (int) Math.round(1_000_000_000.0 / (double) p99Nanos);
                    onePercentLow = activeOnePercentLow;
                }
            }
        } else {
            // Window is inactive/unfocused: preserve active gameplay metrics and update currentFps
            currentFps = (int) Math.round(1_000_000_000.0 / (double) delta);
        }
    }

    public int getActiveGameplayFps() {
        return activeGameplayFps;
    }

    public int getActiveOnePercentLow() {
        return activeOnePercentLow;
    }

    public double getActiveFrametimeMs() {
        return activeFrametimeMs;
    }

    public int getCurrentFps() {
        return currentFps;
    }

    public int getOnePercentLow() {
        return activeOnePercentLow > 0 ? activeOnePercentLow : onePercentLow;
    }

    public double getCurrentFrametimeMs() {
        return currentFrametimeMs;
    }

    public long getLastFrameDeltaNanos() {
        return lastFrameDeltaNanos;
    }
}
