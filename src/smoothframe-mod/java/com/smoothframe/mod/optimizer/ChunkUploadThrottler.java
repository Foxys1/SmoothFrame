package com.smoothframe.mod.optimizer;

import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.chunk.SectionRenderDispatcher;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.neoforge.client.event.RenderFrameEvent;

/**
 * Monitors pending chunk section uploads for telemetry and performance diagnostics
 * without inducing artificial thread stalls or frametime hitches on the render loop.
 */
public class ChunkUploadThrottler {
    private static final ChunkUploadThrottler INSTANCE = new ChunkUploadThrottler();

    public static ChunkUploadThrottler getInstance() {
        return INSTANCE;
    }

    private volatile int pendingUploads = 0;

    public ChunkUploadThrottler() {
    }

    @SubscribeEvent
    public void onRenderFramePre(RenderFrameEvent.Pre event) {
        Minecraft mc = Minecraft.getInstance();
        if (mc == null || mc.levelRenderer == null) {
            pendingUploads = 0;
            return;
        }

        SectionRenderDispatcher dispatcher = mc.levelRenderer.getSectionRenderDispatcher();
        if (dispatcher == null) {
            pendingUploads = 0;
            return;
        }

        pendingUploads = dispatcher.getToUpload();
    }

    public int getPendingUploads() {
        return pendingUploads;
    }
}
