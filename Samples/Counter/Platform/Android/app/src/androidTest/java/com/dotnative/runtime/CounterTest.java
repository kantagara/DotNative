package com.dotnative.runtime;

import android.test.ActivityInstrumentationTestCase2;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;
import android.widget.Button;
import android.os.SystemClock;

/** End-to-end JNI → C# State → binary diff → Taffy → Android native controls. */
@SuppressWarnings("deprecation")
public final class CounterTest extends ActivityInstrumentationTestCase2<NativeHost> {
    public CounterTest(){super(NativeHost.class);}
    private View find(View view,String text){
        if(view instanceof TextView && ((TextView)view).getText().toString().equals(text))return view;
        if(view instanceof ViewGroup){ViewGroup group=(ViewGroup)view;for(int i=0;i<group.getChildCount();i++){View v=find(group.getChildAt(i),text);if(v!=null)return v;}}
        return null;
    }
    private View awaitText(String text){
        View[] found={null};long deadline=SystemClock.uptimeMillis()+5000;
        while(found[0]==null && SystemClock.uptimeMillis()<deadline){
            getInstrumentation().runOnMainSync(()->found[0]=find(getActivity().getWindow().getDecorView(),text));
            if(found[0]==null)SystemClock.sleep(16);
        }
        assertNotNull("Missing native text: "+text,found[0]);return found[0];
    }
    public void testCounterAndStableControls(){
        getActivity();
        View label=awaitText("Count: 0");View increment=awaitText("Increment");View reset=awaitText("Reset");
        assertTrue(increment instanceof Button);
        assertEquals(android.graphics.Color.BLACK,((TextView)label).getCurrentTextColor());
        assertEquals(android.graphics.Color.WHITE,((TextView)increment).getCurrentTextColor());
        for(int i=1;i<=3;i++){
            getInstrumentation().runOnMainSync(()->increment.performClick());
            assertSame("The count label must retain native identity",label,awaitText("Count: "+i));
            assertSame(increment,awaitText("Increment"));
        }
        getInstrumentation().runOnMainSync(()->reset.performClick());
        assertSame(label,awaitText("Count: 0"));
        for(int i=1;i<=3;i++){
            getInstrumentation().runOnMainSync(()->increment.performClick());awaitText("Count: "+i);
        }
        // The current native runtime owns one session per process. Exercise
        // scrolling in this activity rather than starting a second session.
        verifyHorizontalScrollExtentAndAxisReplacement();
        SystemClock.sleep(100);
        try(java.io.FileOutputStream file=getActivity().openFileOutput("counter.png",0)){
            android.graphics.Bitmap screenshot=getInstrumentation().getUiAutomation().takeScreenshot();
            assertNotNull(screenshot);screenshot.compress(android.graphics.Bitmap.CompressFormat.PNG,100,file);
        }catch(java.io.IOException error){throw new AssertionError(error);}
    }
    private void verifyHorizontalScrollExtentAndAxisReplacement() {
        getActivity();
        getInstrumentation().runOnMainSync(() -> {
            NativeHost host=getActivity();
            ViewGroup scroll=(ViewGroup)host.create(900001,4);
            View child=host.create(900002,1);
            host.attach(scroll,child);
            host.frame(child,0,0,600,50);
            host.scrollPolicy(scroll,true,1);
            int width=Math.round(100*host.getResources().getDisplayMetrics().density);
            int height=Math.round(80*host.getResources().getDisplayMetrics().density);
            scroll.measure(View.MeasureSpec.makeMeasureSpec(width,View.MeasureSpec.EXACTLY),
                View.MeasureSpec.makeMeasureSpec(height,View.MeasureSpec.EXACTLY));
            scroll.layout(0,0,width,height);
            assertTrue(scroll.getChildAt(0) instanceof android.widget.HorizontalScrollView);
            android.widget.HorizontalScrollView horizontal=(android.widget.HorizontalScrollView)scroll.getChildAt(0);
            ViewGroup document=(ViewGroup)horizontal.getChildAt(0);
            assertSame(child,document.getChildAt(0));
            assertTrue(document.getMeasuredWidth()>width);
            horizontal.scrollTo(width,0);
            assertTrue(horizontal.getScrollX()>0);
            host.scrollPolicy(scroll,false,0);
            assertTrue(scroll.getChildAt(0) instanceof androidx.core.widget.NestedScrollView);
            assertSame(child,((ViewGroup)((ViewGroup)scroll.getChildAt(0)).getChildAt(0)).getChildAt(0));
            host.scrollPolicy(scroll,true,1);
            assertSame(child,((ViewGroup)((ViewGroup)scroll.getChildAt(0)).getChildAt(0)).getChildAt(0));
        });
    }
}
