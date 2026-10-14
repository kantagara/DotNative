package com.dotnative.runtime;

import android.test.ActivityInstrumentationTestCase2;
import android.os.SystemClock;
import android.view.*;
import android.widget.*;

/** System-injected input, real renderer, remote C# host; no performClick/setPressed callbacks. */
@SuppressWarnings("deprecation")
public final class InteractionInputTest extends ActivityInstrumentationTestCase2<NativeHost> {
    public InteractionInputTest(){super(NativeHost.class);}
    interface Check { boolean run(); }
    private void await(String reason, Check check){
        long deadline=SystemClock.uptimeMillis()+6000; boolean[] passed={false};
        while(SystemClock.uptimeMillis()<deadline){
            getInstrumentation().runOnMainSync(()->passed[0]=check.run());
            if(passed[0])return;SystemClock.sleep(20);
        }
        fail(reason);
    }
    private View find(View view,String text){
        if(view instanceof TextView && ((TextView)view).getText().toString().equals(text))return view;
        if(view instanceof ViewGroup){ViewGroup group=(ViewGroup)view;for(int i=0;i<group.getChildCount();i++){View result=find(group.getChildAt(i),text);if(result!=null)return result;}}
        return null;
    }
    private View text(String value){
        View[] result={null};await("Missing "+value,()->(result[0]=find(getActivity().getWindow().getDecorView(),value))!=null);return result[0];
    }
    private float[] center(View view){float[] p=new float[2];getInstrumentation().runOnMainSync(()->{int[] xy=new int[2];view.getLocationOnScreen(xy);p[0]=xy[0]+view.getWidth()/2f;p[1]=xy[1]+view.getHeight()/2f;});return p;}
    private void touch(int action,long down,float[] p){
        MotionEvent e=MotionEvent.obtain(down,SystemClock.uptimeMillis(),action,p[0],p[1],0);e.setSource(InputDevice.SOURCE_TOUCHSCREEN);
        assertTrue("Input injection failed",getInstrumentation().getUiAutomation().injectInputEvent(e,true));e.recycle();
    }
    private void tap(View view){float[] p=center(view);long down=SystemClock.uptimeMillis();touch(MotionEvent.ACTION_DOWN,down,p);SystemClock.sleep(60);touch(MotionEvent.ACTION_UP,down,p);}
    private View findType(View view,Class<?> type){
        if(type.isInstance(view))return view;
        if(view instanceof ViewGroup){ViewGroup group=(ViewGroup)view;for(int i=0;i<group.getChildCount();i++){View found=findType(group.getChildAt(i),type);if(found!=null)return found;}}return null;
    }
    private View type(Class<?> type){View[] found={null};await("Missing "+type.getSimpleName(),()->(found[0]=findType(getActivity().getWindow().getDecorView(),type))!=null);return found[0];}
    private void hover(float[] p){
        MotionEvent.PointerProperties prop=new MotionEvent.PointerProperties();prop.id=0;prop.toolType=MotionEvent.TOOL_TYPE_MOUSE;
        MotionEvent.PointerCoords coords=new MotionEvent.PointerCoords();coords.x=p[0];coords.y=p[1];
        long now=SystemClock.uptimeMillis();MotionEvent e=MotionEvent.obtain(now,now,MotionEvent.ACTION_HOVER_MOVE,1,new MotionEvent.PointerProperties[]{prop},new MotionEvent.PointerCoords[]{coords},0,0,1,1,0,0,InputDevice.SOURCE_MOUSE,0);
        assertTrue(getInstrumentation().getUiAutomation().injectInputEvent(e,true));e.recycle();
    }
    private void opacity(View view,float expected){await("Expected native opacity "+expected,()->Math.abs(((View)view.getParent()).getAlpha()-expected)<.02f);}
    public void testSystemInputRoundTrip(){
        getActivity();View radio=text("Test radio"),button=text("Test button"),reset=text("Reset");
        opacity(radio,1);float[] p=center(radio);long down=SystemClock.uptimeMillis();
        touch(MotionEvent.ACTION_DOWN,down,p);opacity(radio,.4f);text("Selections: 0");
        touch(MotionEvent.ACTION_UP,down,p);text("Selections: 1");opacity(radio,.65f);assertSame(radio,text("Test radio"));
        tap(reset);text("Selections: 0");opacity(radio,1);
        down=SystemClock.uptimeMillis();touch(MotionEvent.ACTION_DOWN,down,p);opacity(radio,.4f);
        touch(MotionEvent.ACTION_CANCEL,down,p);opacity(radio,1);text("Selections: 0");
        p=center(button);down=SystemClock.uptimeMillis();touch(MotionEvent.ACTION_DOWN,down,p);opacity(button,.4f);
        float[] outside={p[0],p[1]+button.getHeight()*2};touch(MotionEvent.ACTION_MOVE,down,outside);
        touch(MotionEvent.ACTION_UP,down,outside);opacity(button,1);text("Clicks: 0");
        tap(button);text("Clicks: 1");assertSame(button,text("Test button"));
        tap(text("Disable soon"));p=center(radio);down=SystemClock.uptimeMillis();touch(MotionEvent.ACTION_DOWN,down,p);opacity(radio,.4f);
        opacity(radio,.2f);touch(MotionEvent.ACTION_UP,down,p);text("Selections: 0");
        tap(reset);opacity(radio,1);tap(radio);text("Selections: 1");opacity(radio,.65f);
        tap(reset);opacity(radio,1);
        View[] hoverControls={radio,button,text("Test field"),type(android.widget.Switch.class),type(SeekBar.class)};
        float[] hoverAlpha={.8f,.8f,.75f,.7f,.7f};
        for(int i=0;i<hoverControls.length;i++){
            hover(center(hoverControls[i]));opacity(hoverControls[i],hoverAlpha[i]);
            hover(new float[]{1,1});opacity(hoverControls[i],1);
        }
        System.out.println("PASS INPUT Android: radio down/up and rendered alpha; cancellation; button drag-out; disabled while held; re-enable; mouse hover enter/exit on five controls; stable native identity");
    }
}
