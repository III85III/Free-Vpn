package io.github.iii85iii.vpn;

import android.animation.ValueAnimator;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PathMeasure;
import android.graphics.RadialGradient;
import android.graphics.Shader;
import android.view.View;
import android.view.animation.LinearInterpolator;

/**
 * Ultra-premium 3D sci-fi infinity symbol (∞) with continuous flowing liquid-gold energy.
 * Strictly adheres to luxury styling:
 * - Glossy pearl-white ceramic and polished silver outer surfaces
 * - Deep jet-black and metallic charcoal inner mechanical panels
 * - Luminous metallic gold and warm amber energy flowing through internal channels
 * - Absolutely NO blue, cyan, purple, green, or neon colors
 * - Central crossover flare when energy streams pass through the hub
 */
public class InfinityLoopView extends View {
    public static final int STATE_DISCONNECTED = 0;
    public static final int STATE_CONNECTING = 1;
    public static final int STATE_CONNECTED = 2;
    public static final int STATE_ERROR = 3;

    private int state = STATE_DISCONNECTED;
    private ValueAnimator animator;
    private float progress = 0f;

    private final Path infinityPath = new Path();
    private final PathMeasure pathMeasure = new PathMeasure();
    private float pathLength = 0f;

    private final Paint shadowPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint outerBevelPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint trackPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint innerChannelPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint railPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint armorTickPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint energyGlowPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint energyMidPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint energyCorePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint headFlarePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint crossoverFlarePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint glassSheenPaint = new Paint(Paint.ANTI_ALIAS_FLAG);

    public InfinityLoopView(Context context) {
        super(context);
        init();
    }

    private void init() {
        shadowPaint.setStyle(Paint.Style.STROKE);
        shadowPaint.setStrokeCap(Paint.Cap.ROUND);
        shadowPaint.setStrokeJoin(Paint.Join.ROUND);
        shadowPaint.setColor(0x55000000);

        outerBevelPaint.setStyle(Paint.Style.STROKE);
        outerBevelPaint.setStrokeCap(Paint.Cap.ROUND);
        outerBevelPaint.setStrokeJoin(Paint.Join.ROUND);
        outerBevelPaint.setColor(0x40CBD5E1);

        trackPaint.setStyle(Paint.Style.STROKE);
        trackPaint.setStrokeCap(Paint.Cap.ROUND);
        trackPaint.setStrokeJoin(Paint.Join.ROUND);
        trackPaint.setColor(0xFF1E2530);

        innerChannelPaint.setStyle(Paint.Style.STROKE);
        innerChannelPaint.setStrokeCap(Paint.Cap.ROUND);
        innerChannelPaint.setStrokeJoin(Paint.Join.ROUND);
        innerChannelPaint.setColor(0xFF080B10);

        railPaint.setStyle(Paint.Style.STROKE);
        railPaint.setStrokeCap(Paint.Cap.ROUND);
        railPaint.setColor(0x28D4AF37);

        armorTickPaint.setStyle(Paint.Style.STROKE);
        armorTickPaint.setStrokeCap(Paint.Cap.ROUND);
        armorTickPaint.setColor(0x35E2E8F0);

        glassSheenPaint.setStyle(Paint.Style.STROKE);
        glassSheenPaint.setStrokeCap(Paint.Cap.ROUND);
        glassSheenPaint.setColor(0x18FFFFFF);

        energyGlowPaint.setStyle(Paint.Style.STROKE);
        energyGlowPaint.setStrokeCap(Paint.Cap.ROUND);

        energyMidPaint.setStyle(Paint.Style.STROKE);
        energyMidPaint.setStrokeCap(Paint.Cap.ROUND);

        energyCorePaint.setStyle(Paint.Style.STROKE);
        energyCorePaint.setStrokeCap(Paint.Cap.ROUND);

        headFlarePaint.setStyle(Paint.Style.FILL);
        crossoverFlarePaint.setStyle(Paint.Style.FILL);

        startAnimation(3000);
    }

    public void setState(int newState) {
        if (this.state == newState) return;
        this.state = newState;
        long duration = (state == STATE_CONNECTING) ? 1400 : (state == STATE_CONNECTED) ? 2000 : 3200;
        startAnimation(duration);
        invalidate();
    }

    private void startAnimation(long duration) {
        if (animator != null) {
            animator.cancel();
        }
        animator = ValueAnimator.ofFloat(0f, 1f);
        animator.setDuration(duration);
        animator.setRepeatCount(ValueAnimator.INFINITE);
        animator.setInterpolator(new LinearInterpolator());
        animator.addUpdateListener(animation -> {
            progress = (Float) animation.getAnimatedValue();
            invalidate();
        });
        if (isAttachedToWindow() || getVisibility() == VISIBLE) {
            animator.start();
        }
    }

    @Override
    protected void onAttachedToWindow() {
        super.onAttachedToWindow();
        if (animator != null && !animator.isRunning()) animator.start();
    }

    @Override
    protected void onDetachedFromWindow() {
        super.onDetachedFromWindow();
        if (animator != null) animator.cancel();
    }

    @Override
    protected void onVisibilityChanged(View changedView, int visibility) {
        super.onVisibilityChanged(changedView, visibility);
        if (animator == null) return;
        if (visibility == VISIBLE && !animator.isRunning()) {
            animator.start();
        } else if (visibility != VISIBLE && animator.isRunning()) {
            animator.cancel();
        }
    }

    @Override
    protected void onSizeChanged(int w, int h, int oldw, int oldh) {
        super.onSizeChanged(w, h, oldw, oldh);
        buildInfinityPath(w, h);
    }

    private void buildInfinityPath(int w, int h) {
        if (w <= 0 || h <= 0) return;
        infinityPath.reset();

        float cx = w / 2f;
        float cy = h / 2f;
        // Lemniscate of Bernoulli: y_max = scaleY / (2 * sqrt(2)) ~= 0.3535 * scaleY
        // To give a loop height of ~0.62 * h:
        float scaleX = (w / 2f) * 0.82f;
        float scaleY = (h / 2f) * 1.80f;

        int points = 240;
        for (int i = 0; i <= points; i++) {
            double t = i * 2.0 * Math.PI / points;
            double sinT = Math.sin(t);
            double cosT = Math.cos(t);
            double denom = 1.0 + sinT * sinT;
            float x = cx + (float) (scaleX * cosT / denom);
            float y = cy + (float) (scaleY * sinT * cosT / denom);
            if (i == 0) infinityPath.moveTo(x, y);
            else infinityPath.lineTo(x, y);
        }
        infinityPath.close();
        pathMeasure.setPath(infinityPath, true);
        pathLength = pathMeasure.getLength();
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        if (pathLength <= 0) return;

        float cx = getWidth() / 2f;
        float cy = getHeight() / 2f;
        float density = getResources().getDisplayMetrics().density;

        // 1. Ambient Drop Shadow
        shadowPaint.setStrokeWidth(26f * density);
        canvas.save();
        canvas.translate(0, 3f * density);
        canvas.drawPath(infinityPath, shadowPaint);
        canvas.restore();

        // 2. Outer Sculpted Ceramic / Silver Armor Ribbons
        trackPaint.setStrokeWidth(22f * density);
        canvas.drawPath(infinityPath, trackPaint);

        // 3. Precision Beveled Pearl-Silver Edges
        outerBevelPaint.setStrokeWidth(23.5f * density);
        canvas.drawPath(infinityPath, outerBevelPaint);

        // 4. Segmented Mechanical Armor Ribs (delicate perpendicular ticks)
        armorTickPaint.setStrokeWidth(1.2f * density);
        float[] pos = new float[2];
        float[] tan = new float[2];
        int numTicks = 32;
        for (int i = 0; i < numTicks; i++) {
            float dist = (i * pathLength) / numTicks;
            pathMeasure.getPosTan(dist, pos, tan);
            // Normal vector perpendicular to tangent (-tan.y, tan.x)
            float nx = -tan[1];
            float ny = tan[0];
            float rInner = 6.5f * density;
            float rOuter = 10.5f * density;
            canvas.drawLine(pos[0] + nx * rInner, pos[1] + ny * rInner, pos[0] + nx * rOuter, pos[1] + ny * rOuter, armorTickPaint);
            canvas.drawLine(pos[0] - nx * rInner, pos[1] - ny * rInner, pos[0] - nx * rOuter, pos[1] - ny * rOuter, armorTickPaint);
        }

        // 5. Deep Jet-Black Inner Mechanical Channel
        innerChannelPaint.setStrokeWidth(11f * density);
        canvas.drawPath(infinityPath, innerChannelPaint);

        // 6. Luminous Gold Channel Guide Rails
        railPaint.setStrokeWidth(12f * density);
        canvas.drawPath(infinityPath, railPaint);

        // 7. Glass Sheen / Specular Curved Highlight
        glassSheenPaint.setStrokeWidth(5f * density);
        canvas.save();
        canvas.translate(0, -1.5f * density);
        canvas.drawPath(infinityPath, glassSheenPaint);
        canvas.restore();

        // 8. Flowing Liquid Gold Energy Streams (Primary and Intertwined Secondary Stream)
        drawEnergyStream(canvas, progress, density, true);
        drawEnergyStream(canvas, (progress + 0.5f) % 1.0f, density, false);

        // 9. Central Crossover Luminous Hub Flare
        // Central crossover occurs when progress is near 0.25 or 0.75
        float distToCenter1 = Math.abs(progress - 0.25f);
        if (distToCenter1 > 0.5f) distToCenter1 = 1f - distToCenter1;
        float distToCenter2 = Math.abs(progress - 0.75f);
        if (distToCenter2 > 0.5f) distToCenter2 = 1f - distToCenter2;
        float minDist = Math.min(distToCenter1, distToCenter2);

        float crossoverIntensity = Math.max(0f, 1f - minDist * 10f);
        if (crossoverIntensity > 0f) {
            float flareRadius = (16f + crossoverIntensity * 16f) * density;
            crossoverFlarePaint.setShader(new RadialGradient(cx, cy, flareRadius,
                Color.argb((int)(200 * crossoverIntensity), 255, 248, 220),
                Color.argb(0, 212, 175, 55),
                Shader.TileMode.CLAMP));
            canvas.drawCircle(cx, cy, flareRadius, crossoverFlarePaint);

            // Delicate horizontal/slanted gold energy streak across the crossover
            Paint streakPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
            streakPaint.setColor(Color.argb((int)(160 * crossoverIntensity), 255, 235, 170));
            streakPaint.setStrokeWidth(1.5f * density);
            canvas.drawLine(cx - flareRadius * 0.9f, cy, cx + flareRadius * 0.9f, cy, streakPaint);
        }
    }

    private void drawEnergyStream(Canvas canvas, float prog, float density, boolean isPrimary) {
        float streamFraction = (state == STATE_CONNECTING) ? 0.40f : (state == STATE_CONNECTED) ? 0.38f : 0.32f;
        float streamLen = pathLength * streamFraction;
        float headDist = (prog * pathLength) % pathLength;
        float tailDist = headDist - streamLen;

        Path streamPath = new Path();
        if (tailDist >= 0) {
            pathMeasure.getSegment(tailDist, headDist, streamPath, true);
        } else {
            pathMeasure.getSegment(pathLength + tailDist, pathLength, streamPath, true);
            pathMeasure.getSegment(0, headDist, streamPath, true);
        }

        // Palette: strictly warm amber and metallic liquid gold (no blue, green, or neon)
        int glowColor = (state == STATE_CONNECTED) ? 0x66FFC107 : (state == STATE_CONNECTING) ? 0x55FFB300 : 0x40D4AF37;
        int midColor = (state == STATE_CONNECTED) ? 0xFFF5D061 : (state == STATE_CONNECTING) ? 0xFFFFCA28 : 0xEEF5C042;
        int coreColor = 0xFFFFFDF5;

        // 1. Broad Volumetric Ambient Gold Glow
        energyGlowPaint.setStrokeWidth((isPrimary ? 24f : 18f) * density);
        energyGlowPaint.setColor(glowColor);
        canvas.drawPath(streamPath, energyGlowPaint);

        // 2. Dense Liquid Gold Core Body
        energyMidPaint.setStrokeWidth((isPrimary ? 10f : 8f) * density);
        energyMidPaint.setColor(midColor);
        canvas.drawPath(streamPath, energyMidPaint);

        // 3. White-Hot Energy Filament
        energyCorePaint.setStrokeWidth((isPrimary ? 4f : 3f) * density);
        energyCorePaint.setColor(coreColor);
        canvas.drawPath(streamPath, energyCorePaint);

        // 4. Leading Energy Spark Flare
        float[] pos = new float[2];
        float[] tan = new float[2];
        pathMeasure.getPosTan(headDist, pos, tan);

        float flareRadius = (isPrimary ? 8.5f : 6.5f) * density;
        headFlarePaint.setColor(0x99FFD700);
        canvas.drawCircle(pos[0], pos[1], flareRadius, headFlarePaint);
        headFlarePaint.setColor(0xFFFFFFFF);
        canvas.drawCircle(pos[0], pos[1], flareRadius * 0.55f, headFlarePaint);

        // 5. Motion Trail Micro-Sparks
        if (isPrimary) {
            float[] trailPos = new float[2];
            float[] trailTan = new float[2];
            float trailStep = streamLen * 0.22f;
            for (int k = 1; k <= 3; k++) {
                float trailDist = headDist - k * trailStep;
                if (trailDist < 0) trailDist += pathLength;
                pathMeasure.getPosTan(trailDist, trailPos, trailTan);
                headFlarePaint.setColor(Color.argb(180 - k * 45, 255, 220, 120));
                canvas.drawCircle(trailPos[0], trailPos[1], (3.5f - k * 0.8f) * density, headFlarePaint);
            }
        }
    }
}
