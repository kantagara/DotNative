package com.dotnative.runtime;

import android.test.ActivityInstrumentationTestCase2;
import android.os.SystemClock;
import android.view.*;
import android.widget.*;

@SuppressWarnings("deprecation")
public final class NavigationInputTest extends ActivityInstrumentationTestCase2<NativeHost> {
    public NavigationInputTest() { super(NativeHost.class); }
    private View find(View view, String text) {
        if (!view.isShown()) return null;
        if (view instanceof TextView && ((TextView)view).getText().toString().equals(text)) return view;
        if (view instanceof ViewGroup) {
            ViewGroup group = (ViewGroup)view;
            for (int i=0; i<group.getChildCount(); i++) {
                View found = find(group.getChildAt(i), text);
                if (found != null) return found;
            }
        }
        return null;
    }
    private View text(String value, long timeout) {
        View[] found = { null };
        long deadline = SystemClock.uptimeMillis() + timeout;
        while (SystemClock.uptimeMillis() < deadline) {
            getInstrumentation().runOnMainSync(() -> found[0] = find(getActivity().getWindow().getDecorView(), value));
            if (found[0] != null) return found[0];
            SystemClock.sleep(30);
        }
        fail("Missing visible label: " + value);
        return null;
    }
    private void tap(View view) {
        float[] xy = new float[2];
        getInstrumentation().runOnMainSync(() -> {
            int[] location = new int[2]; view.getLocationOnScreen(location);
            xy[0] = location[0] + view.getWidth()/2f; xy[1] = location[1] + view.getHeight()/2f;
        });
        long down = SystemClock.uptimeMillis();
        for (int action : new int[] { MotionEvent.ACTION_DOWN, MotionEvent.ACTION_UP }) {
            MotionEvent event = MotionEvent.obtain(down, SystemClock.uptimeMillis(), action, xy[0], xy[1], 0);
            event.setSource(InputDevice.SOURCE_TOUCHSCREEN);
            assertTrue(getInstrumentation().getUiAutomation().injectInputEvent(event, true));
            event.recycle(); SystemClock.sleep(60);
        }
    }
    private View page(View view) {
        while (view != null) {
            try {
                java.lang.reflect.Field kind = view.getClass().getDeclaredField("kind");
                kind.setAccessible(true);
                if (kind.getInt(view) == 11) return view;
            } catch (ReflectiveOperationException ignored) { }
            view = view.getParent() instanceof View ? (View)view.getParent() : null;
        }
        throw new AssertionError("Missing retained navigation page");
    }
    private void awaitNavigationIdle(View page) {
        View navigation = (View)page.getParent();
        boolean[] busy = { true };
        long deadline = SystemClock.uptimeMillis() + 5000;
        while (SystemClock.uptimeMillis() < deadline) {
            getInstrumentation().runOnMainSync(() -> {
                try {
                    java.lang.reflect.Field field = navigation.getClass().getDeclaredField("navigationBusy");
                    field.setAccessible(true); busy[0] = field.getBoolean(navigation);
                } catch (ReflectiveOperationException ex) { throw new AssertionError(ex); }
            });
            if (!busy[0]) { SystemClock.sleep(150); return; }
            SystemClock.sleep(25);
        }
        fail("Native navigation never completed");
    }
    private void verifyFadeProgress() {
        View from = page(text("Open curve probe", 5000));
        tap(text("Open curve probe", 5000));
        View to = page(text("Detail ready", 5000));
        float previous = 0;
        int intermediate = 0;
        boolean sampled = false;
        long deadline = SystemClock.uptimeMillis() + 3000;
        while (SystemClock.uptimeMillis() < deadline) {
            float[] alpha = new float[2];
            getInstrumentation().runOnMainSync(() -> { alpha[0] = to.getAlpha(); alpha[1] = from.getAlpha(); });
            assertTrue("Fade regressed", alpha[0] >= previous - .02f);
            assertEquals("Outgoing page must stay opaque", 1f, alpha[1], .001f);
            if (alpha[0] > .01f && alpha[0] < .99f) intermediate++;
            if (!sampled && alpha[0] > .2f && alpha[0] < .8f) {
                int[] point = new int[2];
                getInstrumentation().runOnMainSync(() -> { to.getLocationOnScreen(point); point[0] += to.getWidth()*4/5; point[1] += to.getHeight()*3/5; });
                android.graphics.Bitmap image = getInstrumentation().getUiAutomation().takeScreenshot();
                int color = image.getPixel(point[0], point[1]); image.recycle();
                assertTrue("Fade exposed the background", android.graphics.Color.red(color) >= 35 && android.graphics.Color.green(color) >= 115 && android.graphics.Color.blue(color) >= 195);
                sampled = true;
            }
            if (alpha[0] >= .999f) break;
            previous = alpha[0]; SystemClock.sleep(25);
        }
        assertTrue("No intermediate native frames", intermediate > 5);
        assertTrue("No intermediate rendered pixel", sampled);
        SystemClock.sleep(200);
        getInstrumentation().sendKeyDownUpSync(KeyEvent.KEYCODE_BACK);
        text("Open curve probe", 5000);
        awaitNavigationIdle(from);
    }
    private void gestureEvent(int action, long down, float x, float y) {
        MotionEvent event = MotionEvent.obtain(down, SystemClock.uptimeMillis(), action, x, y, 0);
        event.setSource(InputDevice.SOURCE_TOUCHSCREEN);
        assertTrue(getInstrumentation().getUiAutomation().injectInputEvent(event, true));
        event.recycle();
    }
    private void predictiveGesture(View detail, boolean right, boolean cancel, boolean fade) {
        View nav = (View)page(detail).getParent();
        int[] bounds = new int[4];
        getInstrumentation().runOnMainSync(() -> {
            getActivity().getWindow().getDecorView().getLocationOnScreen(bounds);
            bounds[2] = getActivity().getWindow().getDecorView().getWidth();
            bounds[3] = getActivity().getWindow().getDecorView().getHeight();
        });
        float start = right ? bounds[2]-2 : 2;
        float direction = right ? -1 : 1;
        float y = bounds[3]*.65f;
        long down = SystemClock.uptimeMillis();
        gestureEvent(MotionEvent.ACTION_DOWN, down, start, y);
        for(int step=1; step<=12; step++) {
            gestureEvent(MotionEvent.ACTION_MOVE, down, start+direction*bounds[2]*.28f*step/12, y);
            SystemClock.sleep(25);
        }
        SystemClock.sleep(100);
        getInstrumentation().runOnMainSync(() -> {
            try {
                java.lang.reflect.Field fromField = nav.getClass().getDeclaredField("predictiveFrom");
                java.lang.reflect.Field toField = nav.getClass().getDeclaredField("predictiveTo");
                fromField.setAccessible(true); toField.setAccessible(true);
                View from = (View)fromField.get(nav), to = (View)toField.get(nav);
                assertNotNull("OS did not start predictive Back", from);
                assertNotNull(to);
                assertEquals(1f, from.getAlpha(), .001f);
                if(fade) assertTrue("Fade did not track gesture", to.getAlpha()>0 && to.getAlpha()<1);
                else assertTrue("Slide did not track gesture", from.getTranslationX()*direction>0 && Math.abs(from.getTranslationX())<bounds[2]);
            } catch (ReflectiveOperationException ex) { throw new AssertionError(ex); }
        });
        if(cancel) gestureEvent(MotionEvent.ACTION_CANCEL, down, start+direction*bounds[2]*.28f, y);
        else {
            for(int step=1;step<=12;step++) {
                gestureEvent(MotionEvent.ACTION_MOVE, down, start+direction*bounds[2]*(.28f+.55f*step/12),y);
                SystemClock.sleep(15);
            }
            gestureEvent(MotionEvent.ACTION_UP,down,start+direction*bounds[2]*.83f,y);
        }
        awaitNavigationIdle(page(detail));
    }
    private void verifyPredictiveGestures() {
        if(android.os.Build.VERSION.SDK_INT<34) return;
        getActivity();
        text("Disposed: 0",15000);
        int disposed=0;
        for(boolean fade : new boolean[]{false,true}) for(boolean right : new boolean[]{false,true}) {
            String button=fade ? "Open native Back" : "Open slide swipe";
            tap(text(button,5000));
            View detail=text("Detail ready",5000);
            awaitNavigationIdle(page(detail));
            tap(text("Edit retained state",5000));
            text("Retained edits: 1",5000);
            predictiveGesture(detail,right,true,fade);
            text("Retained edits: 1",5000);
            text("Disposed: "+disposed,5000);
            predictiveGesture(detail,right,false,fade);
            disposed++;
            text("Disposed: "+disposed,5000);
            text(button,5000);
        }
        System.out.println("PASS PREDICTIVE Android: OS edge gestures, live slide/fade, both edges, cancellation preserves state/scope, commit disposes exactly once");
    }
    public void testTimingAndSystemBack() {
        getActivity();
        verifyPredictiveGestures();
        tap(text("Run timing suite", 15000));
        text("PASS 24 round trips", 90000);
        verifyFadeProgress();
        for (int i=0; i<3; i++) {
            System.out.println("NAV_BACK_ITERATION " + i);
            View open = text("Open native Back", 5000);
            awaitNavigationIdle(page(open));
            tap(open);
            text("Detail ready", 5000);
            awaitNavigationIdle(page(text("Detail ready", 5000)));
            getInstrumentation().sendKeyDownUpSync(KeyEvent.KEYCODE_BACK);
            text("Open native Back", 5000);
            SystemClock.sleep(900);
        }
        System.out.println("PASS NAVIGATION Android: 24 timed push/pop pairs, scope disposal, fade progress and rendered pixel, three system Back cycles");
    }
}
