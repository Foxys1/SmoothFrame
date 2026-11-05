package com.smoothframe.mod.optimizer;

import net.minecraft.client.Camera;
import net.minecraft.client.Minecraft;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.MobCategory;
import net.minecraft.world.entity.boss.enderdragon.EnderDragon;
import net.minecraft.world.entity.boss.wither.WitherBoss;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.phys.Vec3;
import net.neoforged.bus.api.SubscribeEvent;
import net.neoforged.neoforge.client.event.RenderFrameEvent;
import net.neoforged.neoforge.client.event.RenderLivingEvent;
import org.joml.Vector3f;

/**
 * Ultra-fast, zero-allocation frustum and distance entity culling handler.
 * Safely cancels client-side living entity draw calls when entities are outside
 * the player's viewing frustum or exceed distance thresholds.
 */
public class EntityCullingHandler {
    private static final EntityCullingHandler INSTANCE = new EntityCullingHandler();

    public static EntityCullingHandler getInstance() {
        return INSTANCE;
    }

    private int culledEntityCounter = 0;
    private volatile int lastReportedCulledCount = 0;
    private double cachedEntityScale = 1.0;
    private long lastScaleCheckNanos = 0;

    // Per-frame cached camera state (eliminates 9,000+ Vec3 heap allocations/sec)
    private double camX = 0;
    private double camY = 0;
    private double camZ = 0;
    private float lookX = 0;
    private float lookY = 0;
    private float lookZ = 0;
    private boolean cameraReady = false;

    public EntityCullingHandler() {
    }

    @SubscribeEvent
    public void onRenderFramePre(RenderFrameEvent.Pre event) {
        Minecraft mc = Minecraft.getInstance();
        if (mc == null || mc.gameRenderer == null) {
            cameraReady = false;
            return;
        }

        Camera camera = mc.gameRenderer.getMainCamera();
        if (camera == null) {
            cameraReady = false;
            return;
        }

        Vec3 pos = camera.getPosition();
        Vector3f look = camera.getLookVector();
        if (pos != null && look != null) {
            camX = pos.x;
            camY = pos.y;
            camZ = pos.z;
            lookX = look.x;
            lookY = look.y;
            lookZ = look.z;
            cameraReady = true;
        } else {
            cameraReady = false;
        }
    }

    @SubscribeEvent
    public void onRenderLivingPre(RenderLivingEvent.Pre<?, ?> event) {
        onRenderEntity(event);
    }

    public void onRenderEntity(RenderLivingEvent.Pre<?, ?> event) {
        Minecraft mc = Minecraft.getInstance();
        if (mc == null || mc.player == null || !cameraReady) {
            return;
        }

        LivingEntity entity = event.getEntity();
        if (entity == null || entity == mc.player) {
            return;
        }

        // Never cull the player's mount or vehicle in third-person view
        if (mc.player.getVehicle() == entity || mc.player.getRootVehicle() == entity || entity.hasPassenger(mc.player)) {
            return;
        }

        // Never cull leashed mobs so leash lines do not visually snap or disappear
        if (entity instanceof Mob mob && mob.isLeashed()) {
            return;
        }

        // Preserve players, bosses, custom named entities, and glowing entities
        if (entity instanceof Player ||
            entity instanceof EnderDragon ||
            entity instanceof WitherBoss ||
            (entity.getType() != null && entity.getType().getCategory() == MobCategory.MISC) ||
            entity.hasCustomName() ||
            entity.hasGlowingTag() ||
            entity.isCurrentlyGlowing()) {
            return;
        }

        Vec3 entityPos = entity.position();
        double dx = entityPos.x - camX;
        double dy = entityPos.y - camY;
        double dz = entityPos.z - camZ;
        double distSqr = dx * dx + dy * dy + dz * dz;

        // Never cull entities within immediate proximity (3 blocks)
        if (distSqr <= 9.0) {
            return;
        }

        // Refresh entity distance scaling option at 2 Hz
        long now = System.nanoTime();
        if (now - lastScaleCheckNanos > 500_000_000L) {
            lastScaleCheckNanos = now;
            if (mc.options != null && mc.options.entityDistanceScaling() != null) {
                Double scaleVal = mc.options.entityDistanceScaling().get();
                if (scaleVal != null) {
                    cachedEntityScale = Math.max(0.25, scaleVal.doubleValue());
                }
            }
        }
        double scale = cachedEntityScale;
        double maxDist = 64.0 * scale;
        double maxDistSqr = maxDist * maxDist;

        if (distSqr > maxDistSqr) {
            event.setCanceled(true);
            culledEntityCounter++;
            return;
        }

        // FOV Frustum Culling using cached look vector: dot product check
        double dist = Math.sqrt(distSqr);
        double dot = (dx * lookX + dy * lookY + dz * lookZ) / dist;

        // If the entity is outside the viewing cone (dot < -0.2), cull it
        if (dot < -0.2) {
            event.setCanceled(true);
            culledEntityCounter++;
        }
    }

    public synchronized int getAndResetCulledCount() {
        lastReportedCulledCount = culledEntityCounter;
        culledEntityCounter = 0;
        return lastReportedCulledCount;
    }

    public int getLastCulledCount() {
        return lastReportedCulledCount;
    }
}
