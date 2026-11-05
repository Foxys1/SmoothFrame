package com.smoothframe.mod;

import com.smoothframe.mod.optimizer.DynamicFpsLimiter;
import com.smoothframe.mod.telemetry.TelemetryServer;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.neoforge.event.GameShuttingDownEvent;

/**
 * Handles client lifecycle and shutdown events for SmoothFrameMod.
 */
public class ClientModEvents {
    private static final ClientModEvents INSTANCE = new ClientModEvents();

    public static ClientModEvents getInstance() {
        return INSTANCE;
    }

    public ClientModEvents() {
    }

    @SubscribeEvent
    public void onGameShuttingDown(GameShuttingDownEvent event) {
        // MOD-01: Stop background UDP telemetry broadcaster cleanly
        TelemetryServer.getInstance().stop();

        // MOD-03: Restore original graphics options (framerate limit and particles) and save
        DynamicFpsLimiter limiter = DynamicFpsLimiter.getInstance();
        if (limiter != null) {
            limiter.restoreOriginalSettings();
        }
    }
}
