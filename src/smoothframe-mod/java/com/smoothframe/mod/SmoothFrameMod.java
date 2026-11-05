package com.smoothframe.mod;

import com.smoothframe.mod.optimizer.ChunkUploadThrottler;
import com.smoothframe.mod.optimizer.DynamicFpsLimiter;
import com.smoothframe.mod.optimizer.EntityCullingHandler;
import com.smoothframe.mod.telemetry.FpsTracker;
import com.smoothframe.mod.telemetry.TelemetryServer;
import net.neoforged.api.distmarker.Dist;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.fml.common.Mod;
import net.neoforged.neoforge.common.NeoForge;

/**
 * SmoothFrame NeoForge 1.21.1 In-Game Optimization Engine.
 * Features:
 * - Chunk mesh upload throttling to eliminate camera micro-stutter
 * - Entity distance & FOV frustum culling
 * - Unfocused window dynamic FPS limiting & particle throttling
 * - Real-time UDP loopback performance telemetry streaming
 */
@Mod(value = SmoothFrameMod.MODID, dist = {Dist.CLIENT})
public class SmoothFrameMod {
    public static final String MODID = "smoothframe";
    public static final String VERSION = "1.0.0";

    public SmoothFrameMod(IEventBus modEventBus) {
        System.out.println("[SmoothFrame] Initializing NeoForge 1.21.1 in-game optimization engine...");

        // Start background UDP telemetry broadcaster (127.0.0.1:28511)
        TelemetryServer.getInstance().start();

        // Register client optimization event handlers to NeoForge.EVENT_BUS
        IEventBus gameBus = NeoForge.EVENT_BUS;
        gameBus.register(ClientModEvents.getInstance());
        gameBus.register(FpsTracker.getInstance());
        gameBus.register(EntityCullingHandler.getInstance());
        gameBus.register(DynamicFpsLimiter.getInstance());
        gameBus.register(ChunkUploadThrottler.getInstance());
        gameBus.register(TelemetryServer.getInstance());

        System.out.println("[SmoothFrame] In-game optimization engine & telemetry active.");
    }
}
