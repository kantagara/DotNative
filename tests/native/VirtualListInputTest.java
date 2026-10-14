package com.dotnative.runtime;

import android.test.ActivityInstrumentationTestCase2;
import android.os.SystemClock;
import android.view.*;
import android.widget.*;

@SuppressWarnings("deprecation")
public final class VirtualListInputTest extends ActivityInstrumentationTestCase2<NativeHost> {
    public VirtualListInputTest(){super(NativeHost.class);}
    interface Check {boolean test();}
    private void await(String message,Check test){long deadline=SystemClock.uptimeMillis()+10000;boolean[] ok={false};while(SystemClock.uptimeMillis()<deadline){getInstrumentation().runOnMainSync(()->ok[0]=test.test());if(ok[0])return;SystemClock.sleep(16);}fail(message);}
    private View find(View root,String text){if(root instanceof TextView && ((TextView)root).getText().toString().equals(text))return root;if(root instanceof ViewGroup){ViewGroup group=(ViewGroup)root;for(int i=0;i<group.getChildCount();i++){View found=find(group.getChildAt(i),text);if(found!=null)return found;}}return null;}
    private View text(String value){View[] found={null};await("Missing "+value,()->(found[0]=find(getActivity().getWindow().getDecorView(),value))!=null);return found[0];}
    private void tap(View view){int[] xy=new int[2];getInstrumentation().runOnMainSync(()->view.getLocationOnScreen(xy));long now=SystemClock.uptimeMillis();float x=xy[0]+view.getWidth()/2f,y=xy[1]+view.getHeight()/2f;for(int action:new int[]{MotionEvent.ACTION_DOWN,MotionEvent.ACTION_UP}){MotionEvent e=MotionEvent.obtain(now,SystemClock.uptimeMillis(),action,x,y,0);e.setSource(InputDevice.SOURCE_TOUCHSCREEN);assertTrue(getInstrumentation().getUiAutomation().injectInputEvent(e,true));e.recycle();}}
    private int rows(View root){int count=root instanceof Button && ((Button)root).getText().toString().startsWith("Row ")?1:0;if(root instanceof ViewGroup){ViewGroup group=(ViewGroup)root;for(int i=0;i<group.getChildCount();i++)count+=rows(group.getChildAt(i));}return count;}
    private void rowControls(View root,java.util.Set<View> result){
        if(root instanceof Button && ((Button)root).getText().toString().startsWith("Row ")) result.add(root);
        if(root instanceof ViewGroup){ViewGroup group=(ViewGroup)root;for(int i=0;i<group.getChildCount();i++)rowControls(group.getChildAt(i),result);}
    }
    private View scroller(View root){if(root instanceof androidx.recyclerview.widget.RecyclerView)return root;if(root instanceof ViewGroup){ViewGroup group=(ViewGroup)root;for(int i=0;i<group.getChildCount();i++){View found=scroller(group.getChildAt(i));if(found!=null)return found;}}return null;}
    public void testRapidDirectionChangesKeepViewportPopulated(){
        getActivity();await("Activity has no input focus",()->getActivity().hasWindowFocus());
        text("Ready: 10000 rows");tap(text("Jump 5000"));text("Row 5000");
        androidx.recyclerview.widget.RecyclerView[] list={null};
        getInstrumentation().runOnMainSync(()->list[0]=(androidx.recyclerview.widget.RecyclerView)scroller(getActivity().getWindow().getDecorView()));
        for(int round=0;round<3;round++){
            for(int direction:new int[]{1,-1}){
                getInstrumentation().runOnMainSync(()->list[0].fling(0,direction*12000));
                SystemClock.sleep(250);
                getInstrumentation().runOnMainSync(()->list[0].stopScroll());
                await("Rapid scroll left holes in the realized viewport",()->{
                    java.util.ArrayList<View> filled=new java.util.ArrayList<>();
                    for(int i=0;i<list[0].getChildCount();i++){
                        View cell=list[0].getChildAt(i);
                        if(rows(cell)>0)filled.add(cell);
                    }
                    filled.sort(java.util.Comparator.comparingInt(View::getTop));
                    int covered=0;
                    for(View cell:filled){
                        if(cell.getBottom()<=covered)continue;
                        if(cell.getTop()>covered+2)return false;
                        covered=Math.max(covered,cell.getBottom());
                    }
                    return covered>=list[0].getHeight()-2;
                });
                await("Rapid scroll retained too many native rows",()->rows(getActivity().getWindow().getDecorView())<40);
            }
        }
        tap(text("Jump 5000"));tap(text("Row 5000"));text("Selected 5000");
    }
    private int mutation;
    private void mutate(String action){
        tap(text(action));text("Mutation "+(++mutation));
        await("Mutation layout did not settle",()->{
            androidx.recyclerview.widget.RecyclerView list=(androidx.recyclerview.widget.RecyclerView)scroller(getActivity().getWindow().getDecorView());
            return !list.isLayoutRequested() && !list.hasPendingAdapterUpdates();
        });
    }
    public void testVirtualWindowAndAnchoring(){
        getActivity();await("Activity has no input focus",()->getActivity().hasWindowFocus());text("Ready: 10000 rows");text("Row 0");
        await("Unbounded native row count",()->rows(getActivity().getWindow().getDecorView())<24);
        java.util.Set<View> originalCells=java.util.Collections.newSetFromMap(new java.util.IdentityHashMap<>());
        getInstrumentation().runOnMainSync(()->{ViewGroup recycler=(ViewGroup)scroller(getActivity().getWindow().getDecorView());assertNotNull("Expected RecyclerView",recycler);for(int i=0;i<recycler.getChildCount();i++)originalCells.add(recycler.getChildAt(i));});
        java.util.Set<View> originalControls=java.util.Collections.newSetFromMap(new java.util.IdentityHashMap<>());
        getInstrumentation().runOnMainSync(()->rowControls(getActivity().getWindow().getDecorView(),originalControls));
        tap(text("Jump 5000"));text("Row 5000");
        // Removed holders can enter the pool only after the first layout completes.
        // Exercise another disjoint window to check reuse after that warm-up.
        getInstrumentation().runOnMainSync(()->{ViewGroup recycler=(ViewGroup)scroller(getActivity().getWindow().getDecorView());for(int i=0;i<recycler.getChildCount();i++)originalCells.add(recycler.getChildAt(i));});
        getInstrumentation().runOnMainSync(()->rowControls(getActivity().getWindow().getDecorView(),originalControls));
        tap(text("End"));text("Row 9999");
        await("Row content controls were recreated instead of recycled",()->{
            java.util.Set<View> current=java.util.Collections.newSetFromMap(new java.util.IdentityHashMap<>());
            rowControls(getActivity().getWindow().getDecorView(),current);
            for(View control:current)if(originalControls.contains(control))return true;
            return false;
        });
        await("RecyclerView did not reuse any native cell",()->{ViewGroup recycler=(ViewGroup)scroller(getActivity().getWindow().getDecorView());for(int i=0;i<recycler.getChildCount();i++)if(originalCells.contains(recycler.getChildAt(i)))return true;return false;});
        tap(text("Jump 5000"));View row=text("Row 5000");
        await("Offscreen row retained",()->find(getActivity().getWindow().getDecorView(),"Row 0")==null);

        tap(row);text("Selected 5000");
        int[] before=new int[2];getInstrumentation().runOnMainSync(()->row.getLocationOnScreen(before));
        mutate("Prepend");
        await("Prepend moved anchor or replaced native row",()->{View current=find(getActivity().getWindow().getDecorView(),"Row 5000");if(current!=row)return false;int[] after=new int[2];current.getLocationOnScreen(after);return Math.abs(after[1]-before[1])<=2;});
        int[] nextBefore=new int[2];View next=text("Row 5001");
        getInstrumentation().runOnMainSync(()->next.getLocationOnScreen(nextBefore));
        mutate("Grow");
        await("Native measured row did not grow",()->{int[] xy=new int[2];next.getLocationOnScreen(xy);return xy[1]>nextBefore[1]+30;});
        await("Growing row moved the anchor",()->{int[] xy=new int[2];row.getLocationOnScreen(xy);return Math.abs(xy[1]-before[1])<=2;});
        mutate("Grow");
        await("Native measured row did not shrink",()->{int[] xy=new int[2];next.getLocationOnScreen(xy);return Math.abs(xy[1]-nextBefore[1])<=2;});
        for(int reorder=0;reorder<2;reorder++){
            mutate("Reverse");
            await("Reorder changed native row identity or anchor",()->{View current=find(getActivity().getWindow().getDecorView(),"Row 5000");if(current!=row)return false;int[] xy=new int[2];current.getLocationOnScreen(xy);return Math.abs(xy[1]-before[1])<=2;});
        }
        getInstrumentation().runOnMainSync(()->{androidx.recyclerview.widget.RecyclerView recycler=(androidx.recyclerview.widget.RecyclerView)scroller(getActivity().getWindow().getDecorView());assertFalse("Contained list must not hand off nested flings",recycler.isNestedScrollingEnabled());assertTrue("Unbounded recycler pool",recycler.getRecycledViewPool().getRecycledViewCount(0)<=32);});
        // Real native scroll callback -> C# window -> newly realized native rows.
        getInstrumentation().runOnMainSync(()->scroller(getActivity().getWindow().getDecorView()).scrollBy(0,Math.round(1200*getActivity().getResources().getDisplayMetrics().density)));
        await("Native scroll did not replace window",()->find(getActivity().getWindow().getDecorView(),"Row 5000")==null);
        await("Scrolled list exceeds its window",()->rows(getActivity().getWindow().getDecorView())<24);
        tap(text("End"));text("Row 9999");
        tap(text("Clear"));text("Empty");
        await("Empty list retained native rows",()->rows(getActivity().getWindow().getDecorView())==0);
    }
}
