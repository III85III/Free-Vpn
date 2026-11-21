package io.github.iii85iii.vpn;

import android.animation.ValueAnimator;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.RadialGradient;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.drawable.Drawable;
import android.view.View;
import android.view.animation.LinearInterpolator;

/** Renders engine connection states without inventing connection progress. */
final class OnyxConnectionView extends View {
    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF ring = new RectF();
    private final Drawable emblem;
    private final ValueAnimator motion = ValueAnimator.ofFloat(0f, 1f);
    private RadialGradient halo;
    private int state = InfinityLoopView.STATE_DISCONNECTED;
    private float phase;

    OnyxConnectionView(Context context) {
        super(context);
        emblem = context.getDrawable(R.drawable.onyx_emblem);
        setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO);
        motion.setDuration(2600);
        motion.setRepeatCount(ValueAnimator.INFINITE);
        motion.setInterpolator(new LinearInterpolator());
        motion.addUpdateListener(frame -> {
            phase = (float) frame.getAnimatedValue();
            invalidate();
        });
    }

    void setState(int value) {
        if (state == value) return;
        state = value;
        updateMotion();
        invalidate();
    }

    @Override protected void onAttachedToWindow() {
        super.onAttachedToWindow();
        updateMotion();
    }

    @Override protected void onDetachedFromWindow() {
        motion.cancel();
        super.onDetachedFromWindow();
    }

    @Override protected void onWindowVisibilityChanged(int visibility) {
        super.onWindowVisibilityChanged(visibility);
        if (motion != null) updateMotion();
    }

    private void updateMotion() {
        boolean active = state == InfinityLoopView.STATE_CONNECTING || state == InfinityLoopView.STATE_CONNECTED;
        if (active && isAttachedToWindow() && getWindowVisibility() == VISIBLE && ValueAnimator.areAnimatorsEnabled()) {
            if (!motion.isStarted()) motion.start();
        } else {
            motion.cancel();
            phase = 0f;
        }
    }

    @Override protected void onSizeChanged(int width, int height, int oldWidth, int oldHeight) {
        super.onSizeChanged(width, height, oldWidth, oldHeight);
        float radius = Math.min(width, height) * 0.48f;
        if (radius <= 0f) return;
        float x = width / 2f, y = height / 2f;
        halo = new RadialGradient(x, y, radius, 0x443e5e8c, Color.TRANSPARENT, Shader.TileMode.CLAMP);
        float ringRadius = radius * 0.75f;
        ring.set(x - ringRadius, y - ringRadius, x + ringRadius, y + ringRadius);
        int half = Math.round(ringRadius * 0.61f);
        if (emblem != null) emblem.setBounds(Math.round(x) - half, Math.round(y) - half, Math.round(x) + half, Math.round(y) + half);
    }

    @Override protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        if (halo == null) return;
        float x = ring.centerX(), y = ring.centerY();
        float radius = ring.width() / 2f;
        boolean connected = state == InfinityLoopView.STATE_CONNECTED;
        boolean connecting = state == InfinityLoopView.STATE_CONNECTING;
        float pulse = (float) ((1 + Math.cos(phase * 2 * Math.PI)) / 2);
        paint.setStyle(Paint.Style.FILL);
        paint.setShader(halo);
        paint.setAlpha(connected ? (int) (155 + 100 * pulse) : connecting ? 180 : 85);
        canvas.drawCircle(x, y, radius / 0.75f, paint);
        paint.setShader(null);
        paint.setStyle(Paint.Style.STROKE);
        paint.setStrokeWidth(Math.max(1f, radius / 95f));
        paint.setColor(0x446e7d98);
        canvas.drawOval(ring, paint);
        canvas.drawCircle(x, y, radius * 0.83f, paint);
        paint.setStrokeCap(Paint.Cap.ROUND);
        paint.setStrokeWidth(Math.max(2f, radius / 45f));
        paint.setColor(state == InfinityLoopView.STATE_ERROR ? 0xffd77978 : connected ? 0xff79e2b6 : 0xffd5dce9);
        canvas.drawArc(ring, -90f + phase * 360f, connecting ? 86f : connected ? 360f : 45f, false, paint);
        if (emblem != null) emblem.draw(canvas);
    }
}
