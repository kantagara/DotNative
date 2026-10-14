package com.dotnative.runtime;

import android.test.ActivityInstrumentationTestCase2;
import android.view.View;
import android.view.ViewGroup;
import android.os.SystemClock;
import com.google.android.material.bottomnavigation.BottomNavigationView;

/** Real native control integration, including managed hide and reveal callbacks. */
@SuppressWarnings("deprecation")
public final class BottomNavigationTest extends ActivityInstrumentationTestCase2<NativeHost> {
    public BottomNavigationTest() { super(NativeHost.class); }
    private BottomNavigationView find(View view) {
        if (view instanceof BottomNavigationView) return (BottomNavigationView)view;
        if (view instanceof ViewGroup) {
            ViewGroup group = (ViewGroup)view;
            for (int i = 0; i < group.getChildCount(); i++) {
                BottomNavigationView found = find(group.getChildAt(i));
                if (found != null) return found;
            }
        }
        return null;
    }
    public void testNativeBarSelectionAndManagedHide() {
        getActivity();
        BottomNavigationView[] found = {null};
        long deadline = SystemClock.uptimeMillis() + 10000;
        while (found[0] == null && SystemClock.uptimeMillis() < deadline) {
            getInstrumentation().runOnMainSync(() -> found[0] = find(getActivity().getWindow().getDecorView()));
            if (found[0] == null) SystemClock.sleep(16);
        }
        assertNotNull("Missing native Material bottom navigation", found[0]);
        BottomNavigationView bar = found[0];
        getInstrumentation().runOnMainSync(() -> {
            assertEquals(4, bar.getMenu().size());
            assertTrue(bar.getHeight() > 0);
            assertEquals("Safe-area padding must not be counted twice", 0, bar.getPaddingBottom());
            int[] position = new int[2];
            bar.getLocationOnScreen(position);
            View decor = getActivity().getWindow().getDecorView();
            android.graphics.Insets bars = decor.getRootWindowInsets().getInsets(android.view.WindowInsets.Type.systemBars());
            assertEquals("Bottom bar must meet the system navigation inset", decor.getHeight() - bars.bottom,
                position[1] + bar.getHeight());
            assertEquals(1, bar.getSelectedItemId());
            bar.setSelectedItemId(4); // Hide -> JNI -> C# State -> disabled native menu.
        });
        boolean[] disabled = {false};
        deadline = SystemClock.uptimeMillis() + 5000;
        while (!disabled[0] && SystemClock.uptimeMillis() < deadline) {
            getInstrumentation().runOnMainSync(() -> disabled[0] = !bar.getMenu().getItem(0).isEnabled());
            if (!disabled[0]) SystemClock.sleep(16);
        }
        assertTrue("Managed hide did not disable native items", disabled[0]);
        getInstrumentation().runOnMainSync(() -> {
            assertSame(bar, find(getActivity().getWindow().getDecorView()));
            ViewGroup node = (ViewGroup)bar.getParent();
            getActivity().bottomNavigation(node, 1, new String[]{"First", "Second"}, new int[]{0, 1});
            assertEquals(2, bar.getMenu().size());
            assertEquals(2, bar.getSelectedItemId());
            assertFalse(bar.getMenu().getItem(1).isEnabled());
            getActivity().enabled(node, true);
            assertTrue(bar.getMenu().getItem(1).isEnabled());
        });
    }
}
