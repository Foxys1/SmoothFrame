package com.smoothframe.mod.optimizer;

import com.smoothframe.mod.telemetry.FpsTracker;
import net.minecraft.client.Minecraft;
import net.minecraft.client.ParticleStatus;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.neoforge.client.event.ClientTickEvent;
import net.neoforged.neoforge.event.GameShuttingDownEvent;

/**
 * Dynamically throttles rendering when the game window is unfocused or idle,
 * saving GPU/CPU resources, and automatically scales particle density under load.
 */
public class DynamicFpsLimiter {
    private static final DynamicFpsLimiter INSTANCE = new DynamicFpsLimiter();

    public static DynamicFpsLimiter getInstance() {
        return INSTANCE;
    }

    private static final int UNFOCUSED_FPS_LIMIT = 15;

    private boolean isWindowThrottled = false;
    private int savedUserFpsLimit = 120;

    private boolean isParticleThrottled = false;
    private ParticleStatus savedParticleStatus = ParticleStatus.ALL;
    private int tickCounter = 0;

    public DynamicFpsLimiter() {
        try {
            Runtime.getRuntime().addShutdownHook(new Thread(this::restoreOriginalSettings, "SmoothFrame-FpsRestoreHook"));
        } catch (Exception ignored) {
        }
    }

    /**
     * Restores the user's original framerate limit if it was throttled while unfocused,
     * preventing throttled FPS from persisting in options.txt on shutdown.
     */
    public void restoreFramerateLimit() {
        if (isWindowThrottled) {
            try {
                Minecraft mc = Minecraft.getInstance();
                if (mc != null && mc.options != null && mc.options.framerateLimit() != null) {
                    mc.options.framerateLimit().set(savedUserFpsLimit);
                    isWindowThrottled = false;
                }
            } catch (Throwable ignored) {
            }
        }
    }

    /**
     * Restores both framerate limit and particle settings to the user's original preferences
     * and saves options.txt if modified, preventing permanent settings corruption on shutdown.
     */
    public void restoreOriginalSettings() {
        try {
            Minecraft mc = Minecraft.getInstance();
            if (mc != null && mc.options != null) {
                boolean modified = false;
                if (isWindowThrottled && mc.options.framerateLimit() != null) {
                    mc.options.framerateLimit().set(savedUserFpsLimit);
                    isWindowThrottled = false;
                    modified = true;
                }
                if (isParticleThrottled && mc.options.particles() != null) {
                    mc.options.particles().set(savedParticleStatus);
                    isParticleThrottled = false;
                    modified = true;
                }
                if (modified) {
                    try {
                        mc.options.save();
                    } catch (Throwable ignored) {
                    }
                }
            }
        } catch (Throwable ignored) {
        }
    }

    @SubscribeEvent
    public void onGameShuttingDown(GameShuttingDownEvent event) {
        restoreOriginalSettings();
    }

    @SubscribeEvent
    public void onClientTick(ClientTickEvent.Post event) {
        Minecraft mc = Minecraft.getInstance();
        if (mc == null || mc.options == null) {
            return;
        }

        // MOD-07: Safe increment with wrap-around to prevent integer overflow
        tickCounter = (tickCounter + 1) % 100_000;

        // 1. Unfocused Window Dynamic FPS Limiting
        boolean active = mc.isWindowActive();
        if (!active) {
            if (!isWindowThrottled && mc.options.framerateLimit() != null) {
                Integer currentLimit = mc.options.framerateLimit().get();
                if (currentLimit != null) {
                    savedUserFpsLimit = currentLimit;
                    if (savedUserFpsLimit > UNFOCUSED_FPS_LIMIT) {
                        mc.options.framerateLimit().set(UNFOCUSED_FPS_LIMIT);
                        isWindowThrottled = true;
                    }
                }
            }
        } else {
            if (isWindowThrottled) {
                restoreFramerateLimit();
            }
        }

        // 2. Dynamic Particle Throttling (evaluated every 20 ticks = 1 second)
        // Inspects active gameplay 1% low metrics to prevent false particle degradation while tabbed out
        if (tickCounter % 20 == 0 && mc.options.particles() != null) {
            int low1Percent = FpsTracker.getInstance().getActiveOnePercentLow();
            if (low1Percent <= 0) {
                low1Percent = FpsTracker.getInstance().getOnePercentLow();
            }
            ParticleStatus currentStatus = mc.options.particles().get();
            if (low1Percent > 0 && low1Percent < 30) {
                if (!isParticleThrottled && currentStatus == ParticleStatus.ALL) {
                    savedParticleStatus = currentStatus;
                    mc.options.particles().set(ParticleStatus.DECREASED);
                    isParticleThrottled = true;
                }
            } else if (low1Percent >= 45 && isParticleThrottled) {
                mc.options.particles().set(savedParticleStatus);
                isParticleThrottled = false;
            }
        }
    }
}
