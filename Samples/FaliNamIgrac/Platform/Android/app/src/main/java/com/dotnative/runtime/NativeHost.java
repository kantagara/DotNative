package com.dotnative.runtime;

import android.app.Activity;
import android.os.Bundle;
import android.view.*;
import android.widget.*;
import android.graphics.drawable.GradientDrawable;
import android.util.TypedValue;
import androidx.recyclerview.widget.RecyclerView;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.DiffUtil;

/** Native controls only. Rust owns the model and Taffy layout; C# owns state. */
public final class NativeHost extends Activity implements Choreographer.FrameCallback {
    static { System.loadLibrary(BuildConfig.DOTNATIVE_LIBRARY); }
    private static native int start(NativeHost host, float width, float height);
    private static native int resize(float width, float height);
    private static native int windowInsets(float top, float bottom, float keyboard);
    private static native int tick();
    private static native int stop();
    private static native int activity(int activity);
    private static native int accessibility(int reduceMotion,float fontScale,int highContrast);
    private static native int systemTheme(int theme);
    private static native void click(int id);
    private static native void event(int id,int kind,float value,String text);
    private NodeView mount;
    private FrameLayout safeArea;
    private int chromeBackground;
    private boolean chromeApplied;
    private TextView measureText;
    private Button measureButton;
    // Detached, lazy prototypes: at most one per supported control kind.
    private final android.util.SparseArray<View> measureControls = new android.util.SparseArray<>();
    private boolean started, resumed, visible, destroyed;
    private static boolean runtimeStopping;
    private static final android.os.Handler mainHandler = new android.os.Handler(android.os.Looper.getMainLooper());
    public interface PlatformViewFactory {
        View create(int nodeId);
        default void rebind(View child, int nodeId) {}
        default void detach(View child, int nodeId) {}
    }
    private static final java.util.Map<String, PlatformViewFactory> platformViewFactories = new java.util.HashMap<>();
    public static void registerPlatformView(String type, PlatformViewFactory factory) {
        if (type == null || factory == null || type.isEmpty() || type.length() > 64) throw new IllegalArgumentException("platform view type");
        for (int i = 0; i < type.length(); i++) {
            char c = type.charAt(i);
            boolean ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
            if (!ok) throw new IllegalArgumentException("platform view type");
        }
        platformViewFactories.put(type, factory);
    }
    private float density;
    private static void check(int status) { if(status!=0) throw new IllegalStateException("DotNative native failure: "+status); }
    private void publishWindowInsets(WindowInsets insets) {
        if (!started || insets == null) return;
        android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
        int ime = insets.getInsets(WindowInsets.Type.ime()).bottom;
        check(windowInsets(bars.top / density, bars.bottom / density, ime / density));
    }
    @Override public void onCreate(Bundle state) {
        setTheme(getResources().getIdentifier("NormalTheme", "style", getPackageName()));
        super.onCreate(state);
        density=getResources().getDisplayMetrics().density;
        measureText=new TextView(this); measureButton=new Button(this);
        measureText.setLayoutParams(new ViewGroup.LayoutParams(-2,-2));
        measureButton.setLayoutParams(new ViewGroup.LayoutParams(-2,-2));
        mount=new NodeView(this,0,1);
        mount.setBackgroundColor(0xffffffff);
        safeArea=new FrameLayout(this);
        safeArea.addView(mount,new FrameLayout.LayoutParams(-1,-1));
        safeArea.setOnApplyWindowInsetsListener((v,insets)->{
            publishWindowInsets(insets);
            android.graphics.Insets bars=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());
            v.setPadding(0,0,0,0);
            return new WindowInsets.Builder(insets).setInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout(), android.graphics.Insets.NONE).build();
        });
        safeArea.setWindowInsetsAnimationCallback(new WindowInsetsAnimation.Callback(WindowInsetsAnimation.Callback.DISPATCH_MODE_CONTINUE_ON_SUBTREE) {
            @Override public WindowInsets onProgress(WindowInsets insets, java.util.List<WindowInsetsAnimation> runningAnimations) {
                publishWindowInsets(insets);
                return insets;
            }
        });
        getWindow().setDecorFitsSystemWindows(false);
        getWindow().setStatusBarColor(android.graphics.Color.TRANSPARENT);
        getWindow().setNavigationBarColor(android.graphics.Color.TRANSPARENT);
        getWindow().setNavigationBarContrastEnforced(false);
        getWindow().setStatusBarContrastEnforced(false);
        setContentView(safeArea);
        boolean systemDark = (getResources().getConfiguration().uiMode & android.content.res.Configuration.UI_MODE_NIGHT_MASK) == android.content.res.Configuration.UI_MODE_NIGHT_YES;
        chrome(systemDark ? 0xff121212 : 0xffffffff);
        mount.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob)->{
            if(r-l<=0 || b-t<=0)return;
            if(!started){startWhenReady();}
            else check(resize((r-l)/density,(b-t)/density));
        });
    }
    // Android 15+ paints transparent system bars over the app surface. Updating
    // only statusBarColor cannot theme that surface; the inset owner must match.
    public void chrome(int background) {
        if (chromeApplied && chromeBackground == background) return;
        chromeBackground = background;
        chromeApplied = true;
        safeArea.setBackgroundColor(background);
        mount.setBackgroundColor(background);
        getWindow().setBackgroundDrawable(new android.graphics.drawable.ColorDrawable(background));
        double luminance = 0.2126 * linearChannel((background >> 16) & 255)
            + 0.7152 * linearChannel((background >> 8) & 255)
            + 0.0722 * linearChannel(background & 255);
        // Choose icon contrast from the painted surface, not the OS preference:
        // explicit app themes may differ from the phone's current appearance.
        int mask = WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS
            | WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS;
        WindowInsetsController bars = getWindow().getInsetsController();
        if (bars != null) bars.setSystemBarsAppearance(luminance > 0.179 ? mask : 0, mask);
    }

    private static double linearChannel(int value) {
        double srgb = value / 255.0;
        return srgb <= 0.04045 ? srgb / 12.92 : Math.pow((srgb + 0.055) / 1.055, 2.4);
    }

    private int previousMotion=-1,previousContrast=-1;private float previousFontScale=-1;
    private void syncAccessibility(){
        int motion=android.provider.Settings.Global.getFloat(getContentResolver(),android.provider.Settings.Global.ANIMATOR_DURATION_SCALE,1)==0?1:0;
        float scale=Math.max(.5f,Math.min(5f,getResources().getConfiguration().fontScale));
        int contrast=android.os.Build.VERSION.SDK_INT>=34 && getSystemService(android.app.UiModeManager.class).getContrast()>=.5f?1:0;
        if(motion!=previousMotion || scale!=previousFontScale || contrast!=previousContrast){check(accessibility(motion,scale,contrast));previousMotion=motion;previousFontScale=scale;previousContrast=contrast;}
    }
    private void syncSystemTheme() {
        int mode = getResources().getConfiguration().uiMode & android.content.res.Configuration.UI_MODE_NIGHT_MASK;
        check(systemTheme(mode == android.content.res.Configuration.UI_MODE_NIGHT_YES ? 2 : 1));
    }
    @Override public void onConfigurationChanged(android.content.res.Configuration configuration) {
        super.onConfigurationChanged(configuration); syncSystemTheme();
    }
    @Override public void onStart(){super.onStart();visible=true;if(started)check(activity(1));}
    @Override public void onResume(){super.onResume();resumed=true;if(started)check(activity(0));Choreographer.getInstance().postFrameCallback(this);}
    @Override public void onPause(){if(backNavigation!=null && backNavigation.predictiveFrom!=null){finishPredictive(backNavigation,false);if(backNavigation.navigationAnimation!=null)backNavigation.navigationAnimation.end();}if(started)check(activity(1));resumed=false;Choreographer.getInstance().removeFrameCallback(this);super.onPause();}
    @Override public void onStop(){visible=false;if(started)check(activity(2));super.onStop();}
    @Override public void doFrame(long time){if(started){syncAccessibility();check(tick());}if(resumed)Choreographer.getInstance().postFrameCallback(this);}
    private void startWhenReady() {
        if(destroyed || started || mount.getWidth()<=0 || mount.getHeight()<=0)return;
        if(runtimeStopping){mainHandler.postDelayed(this::startWhenReady,10);return;}
        syncSystemTheme();syncAccessibility();
        try { android.system.Os.setenv("FNI_DATA_DIRECTORY", getFilesDir().getAbsolutePath() + "/FaliNamIgrac", true); }
        catch (android.system.ErrnoException error) { throw new IllegalStateException("Cannot configure application storage", error); }
        int status=start(this,mount.getWidth()/density,mount.getHeight()/density);
        // Replacement Activities may be laid out before their predecessor is
        // destroyed. Wait for its session instead of treating overlap as fatal.
        if(status==1){mainHandler.postDelayed(this::startWhenReady,10);return;}
        check(status);started=true;publishActivation(getIntent());publishWindowInsets(getWindow().getDecorView().getRootWindowInsets());pluginHook("com.dotnative.plugins.GeneratedPlugins", "register", new Class<?>[]{Activity.class}, this);check(activity(resumed?0:visible?1:2));
    }
    private static void pollStop() {
        int status=stop();
        if(status==1){mainHandler.postDelayed(NativeHost::pollStop,10);return;}
        runtimeStopping=false;check(status);
    }
    @Override public void onDestroy(){
        destroyed=true;
        decodedImages.evictAll();
        updateBack(0, false);
        if(started){started=false;runtimeStopping=true;pollStop();}
        pluginHook("com.dotnative.plugins.NativeChannels", "detach", new Class<?>[]{});
        super.onDestroy();
    }

// Optional generated plugin registry; no dependency on any individual plugin.
private static void pluginHook(String type, String method, Class<?>[] arguments, Object... values) {
    try { Class.forName(type).getMethod(method, arguments).invoke(null, values); }
    catch (ClassNotFoundException absent) { /* App has no channel plugins. */ }
    catch (ReflectiveOperationException error) { throw new IllegalStateException("Cannot initialize plugin runtime", error); }
}
@Override public void onRequestPermissionsResult(int request, String[] permissions, int[] grants) {
    super.onRequestPermissionsResult(request, permissions, grants);
    pluginHook("com.dotnative.plugins.NativeChannels", "permissionResult", new Class<?>[]{int.class, String[].class, int[].class}, request, permissions, grants);
}
private void publishActivation(android.content.Intent intent) {
    if (intent == null) return;
    if (android.content.Intent.ACTION_VIEW.equals(intent.getAction()) && intent.getDataString() != null && intent.getDataString().length() <= 16384)
        event(0, 20, 0, intent.getDataString());
    android.os.Bundle extras = intent.getExtras();
    if (extras != null && (extras.containsKey("google.message_id") || extras.containsKey("gcm.message_id"))) {
        org.json.JSONObject data = new org.json.JSONObject();
        for (String key : extras.keySet()) {
            Object value = extras.get(key);
            if (value instanceof String) try { data.put(key, value); } catch (org.json.JSONException ignored) { }
        }
        if (data.toString().length() <= 16384) event(0, 21, 0, data.toString());
    }
}
@Override protected void onNewIntent(android.content.Intent intent) {
    super.onNewIntent(intent);
    setIntent(intent);
    if (started) publishActivation(intent);
    pluginHook("com.dotnative.plugins.NativeChannels", "newIntent", new Class<?>[]{android.content.Intent.class}, intent);
}
@Override protected void onActivityResult(int request, int result, android.content.Intent data) {
    super.onActivityResult(request, result, data);
    pluginHook("com.dotnative.plugins.NativeChannels", "activityResult", new Class<?>[]{int.class, int.class, android.content.Intent.class}, request, result, data);
}

    // Called synchronously by Rust JNI on this Activity's main thread. The Rust
    // registry holds GlobalRefs; Java's ViewGroup owns attached children as usual.
    public View create(int id,int kind){return create(id,kind,false);}
    public View create(int id,int kind,boolean virtualized){return new NodeView(this,id,kind,virtualized);}
    public void virtualList(View view,View[] rows,float[] offsets){((NativeCollection)((NodeView)view).content).update(rows,offsets);}
    public void remove(View view){if(view.getParent()!=null)((ViewGroup)view.getParent()).removeView(view);}
    public void children(View view,View[] children){
        ViewGroup parent=((NodeView)view).childrenHost();
        java.util.Set<View> retained=new java.util.HashSet<>(java.util.Arrays.asList(children));
        // Keep retained editors attached: removing their ancestor transfers Android focus.
        for(int i=parent.getChildCount()-1;i>=0;i--)if(!retained.contains(parent.getChildAt(i)))parent.removeViewAt(i);
        for(View child:children)if(child.getParent()!=parent){remove(child);parent.addView(child);}
    }
    public void clear(View view){((NodeView)view).childrenHost().removeAllViews();}
    public void attach(View parent,View child){remove(child);((NodeView)parent).childrenHost().addView(child);}
    public void root(View view){mount.removeAllViews();if(view!=null){remove(view);mount.addView(view);}}
    private void textStyle(TextView text,String value,float font,int fg){
        if(!text.getText().toString().equals(value)){
            boolean composing=text instanceof EditText && android.view.inputmethod.BaseInputConnection.getComposingSpanStart(((EditText)text).getText())>=0;
            if(!composing){
                if(text instanceof EditText field){
                    int start=field.getSelectionStart(),end=field.getSelectionEnd();
                    boolean atEnd=start==end && end==field.length();
                    field.setText(value);
                    if(start>=0 && end>=0)field.setSelection(atEnd?field.length():Math.min(start,field.length()),atEnd?field.length():Math.min(end,field.length()));
                }else text.setText(value);
            }
        }
        text.setTextSize(TypedValue.COMPLEX_UNIT_SP,font);text.setTextColor(fg);
        if(text instanceof android.widget.RadioButton){text.setSingleLine(true);return;}
        text.setIncludeFontPadding(false);text.setPadding(0,0,0,0);
        text.setMinimumWidth(0);text.setMinimumHeight(0);text.setMinWidth(0);text.setMinHeight(0);
        if(text instanceof EditText){
            // The outer node owns background, radius and padding. Keep the real
            // editor/cursor/selection, but do not draw a second native underline.
            text.setBackground(null);
            text.setGravity((text.getInputType()&android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE)!=0?android.view.Gravity.TOP:android.view.Gravity.CENTER_VERTICAL);
            text.setHintTextColor((fg & 0x00ffffff) | 0x99000000);
            text.setHighlightColor((fg & 0x00ffffff) | 0x33000000);
            android.graphics.drawable.Drawable cursor = ((EditText)text).getTextCursorDrawable();
            if(cursor != null){cursor = cursor.mutate();cursor.setTint(fg);((EditText)text).setTextCursorDrawable(cursor);}
        }
        if(text instanceof Button){((Button)text).setAllCaps(false);text.setSingleLine(true);text.setBackground(null);}
    }
    private int icon(String name){
        switch(name){case "star":return android.R.drawable.btn_star_big_on;case "check":return android.R.drawable.checkbox_on_background;case "info":return android.R.drawable.ic_dialog_info;default:throw new IllegalArgumentException("Unknown icon: "+name);}
    }
    public void configure(View view,String text,float left,float top,float right,float bottom,int bg,float radius,int fg,float font){
        NodeView n=(NodeView)view;n.updating=true;
        try{
            n.paddingLeft=left*density;n.paddingTop=top*density;n.paddingRight=right*density;n.paddingBottom=bottom*density;
            n.boxColor=bg;n.boxRadius=radius*density;applyBox(n);
            if(n.content instanceof TextView)textStyle((TextView)n.content,text,font,fg);
            if(n.kind==9){ImageView image=(ImageView)n.content;image.setImageResource(icon(text));image.setColorFilter(fg);}
        }finally{n.updating=false;}
        n.requestLayout();
    }
    public void typography(View view,String family,int weight,boolean italic,int alignment,int maxLines,int overflow,float letterSpacing,float lineHeight,int decoration){
        NodeView n=(NodeView)view;
        if(n.content instanceof TextView)applyTypography((TextView)n.content,n.kind,family,weight,italic,alignment,maxLines,overflow,letterSpacing,lineHeight,decoration);
    }
    private void applyTypography(TextView text,int kind,String family,int weight,boolean italic,int alignment,int maxLines,int overflow,float letterSpacing,float lineHeight,int decoration){
        android.graphics.Typeface base=baseFont(family);
        text.setTypeface(android.graphics.Typeface.create(base,weight,italic));
        text.setLetterSpacing(letterSpacing*density/text.getTextSize());
        text.setLineSpacing(lineHeight==0?0:lineHeight*density-text.getPaint().getFontSpacing(),1);
        int flags=text.getPaintFlags() & ~(android.graphics.Paint.UNDERLINE_TEXT_FLAG|android.graphics.Paint.STRIKE_THRU_TEXT_FLAG);
        if((decoration&1)!=0)flags|=android.graphics.Paint.UNDERLINE_TEXT_FLAG;
        if((decoration&2)!=0)flags|=android.graphics.Paint.STRIKE_THRU_TEXT_FLAG;
        text.setPaintFlags(flags);
        if(kind==5){
            boolean multiline=(text.getInputType()&android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE)!=0;
            text.setGravity((multiline?android.view.Gravity.TOP:android.view.Gravity.CENTER_VERTICAL)|(alignment==1?android.view.Gravity.CENTER_HORIZONTAL:alignment==2?android.view.Gravity.RIGHT:android.view.Gravity.LEFT));
            return; // TextInputOptions owns wrapping and password transformation.
        }
        int lines=maxLines<0?(kind==2?0:1):maxLines;
        text.setSingleLine(lines==1);text.setMaxLines(lines==0?Integer.MAX_VALUE:lines);
        text.setGravity(android.view.Gravity.CENTER_VERTICAL|(alignment==1?android.view.Gravity.CENTER_HORIZONTAL:alignment==2?android.view.Gravity.RIGHT:android.view.Gravity.LEFT));
        text.setJustificationMode(alignment==3?android.text.Layout.JUSTIFICATION_MODE_INTER_WORD:android.text.Layout.JUSTIFICATION_MODE_NONE);
        int mode=overflow==0?(kind==2?1:5):overflow;
        text.setEllipsize(mode==3?android.text.TextUtils.TruncateAt.START:mode==4?android.text.TextUtils.TruncateAt.MIDDLE:mode==5?android.text.TextUtils.TruncateAt.END:null);
        text.setHorizontallyScrolling(mode==2 || lines==1);
    }

    public void gradient(View view,float[] offsets,int[] colors,int kind,float sx,float sy,float ex,float ey){
        NodeView n=(NodeView)view;n.gradientOffsets=offsets;n.gradientColors=colors;n.gradientKind=kind;
        n.gradientSx=sx;n.gradientSy=sy;n.gradientEx=ex;n.gradientEy=ey;applyBox(n);
    }
    public void projection(View view,float[] values) {
        NodeView node=(NodeView)view;
        node.projection=new android.graphics.Matrix();
        node.projection.setValues(new float[]{values[0],values[4],values[12]*density,
            values[1],values[5],values[13]*density,values[3]/density,values[7]/density,values[15]});
        node.invalidate();
    }
    public void visual(View view,float[] values,int borderColor,int shadowColor,boolean clip){
        NodeView n=(NodeView)view;
        // Transform-only animation keeps its drawable and cached shadow bitmap.
        boolean boxChanged=n.borderWidth!=values[1]*density || n.shadowBlur!=values[2]*density
            || n.shadowX!=values[3]*density || n.shadowY!=values[4]*density
            || n.borderColor!=borderColor || n.shadowColor!=shadowColor || n.clip!=clip;
        n.setAlpha(values[0]);n.borderWidth=values[1]*density;
        n.shadowBlur=values[2]*density;n.shadowX=values[3]*density;n.shadowY=values[4]*density;
        n.setTranslationX(values[5]*density);n.setTranslationY(values[6]*density);
        n.setScaleX(values[7]);n.setScaleY(values[8]);n.setRotation(values[9]);
        n.borderColor=borderColor;n.shadowColor=shadowColor;n.clip=clip;
        n.setClipChildren(clip);n.setClipToPadding(clip);if(boxChanged)applyBox(n);
    }
    private static final java.util.Map<String,android.graphics.Typeface> customFonts=new java.util.HashMap<>();
    private static final java.util.Map<String,java.util.List<android.graphics.fonts.Font>> customFontFaces=new java.util.HashMap<>();
    public void registerFont(String family,byte[] bytes){
        try{
            android.graphics.fonts.Font font=new android.graphics.fonts.Font.Builder((java.nio.ByteBuffer)java.nio.ByteBuffer.allocateDirect(bytes.length).put(bytes).flip()).build();
            String key=family.toLowerCase(java.util.Locale.ROOT);
            java.util.List<android.graphics.fonts.Font> faces=new java.util.ArrayList<>(customFontFaces.getOrDefault(key,java.util.Collections.emptyList()));
            faces.add(font);
            android.graphics.fonts.FontFamily.Builder builder=new android.graphics.fonts.FontFamily.Builder(faces.get(0));
            for(int i=1;i<faces.size();i++)builder.addFont(faces.get(i));
            android.graphics.Typeface typeface=new android.graphics.Typeface.CustomFallbackBuilder(builder.build()).build();
            customFontFaces.put(key,faces);
            customFonts.put(key,typeface);
        }catch(java.io.IOException error){throw new IllegalArgumentException("Invalid font asset",error);}
    }
    private static android.graphics.Typeface baseFont(String family){android.graphics.Typeface custom=customFonts.get(family.toLowerCase(java.util.Locale.ROOT));return custom!=null?custom:family.isEmpty()?android.graphics.Typeface.DEFAULT:android.graphics.Typeface.create(family,0);}
    public void fontVariations(View view,String axes){NodeView n=(NodeView)view;n.fontAxes=axes;if(n.content instanceof TextView)((TextView)n.content).setFontVariationSettings(axes.isEmpty()?null:axes);}
    public void richText(View view,byte[] runs){NodeView n=(NodeView)view;if(n.content instanceof TextView)applyRuns((TextView)n.content,runs,n.fontAxes);}
    private void applyRuns(TextView view,byte[] data,String axes){
        java.nio.ByteBuffer b=java.nio.ByteBuffer.wrap(data).order(java.nio.ByteOrder.LITTLE_ENDIAN);
        int count=b.getInt();String text=view.getText().toString();
        if(count==0){view.setText(text);return;}
        android.text.SpannableString span=new android.text.SpannableString(text);int offset=0;
        for(int i=0;i<count;i++){
            int length=b.getInt();float size=b.getFloat()*density;int color=b.getInt(),weight=b.getInt();boolean italic=b.get()!=0;int decoration=b.get();byte[] familyBytes=new byte[b.getInt()];b.get(familyBytes);String family=new String(familyBytes,java.nio.charset.StandardCharsets.UTF_8);
            android.graphics.Typeface base=baseFont(family);
            span.setSpan(new RunSpan(size,color,android.graphics.Typeface.create(base,weight,italic),decoration,axes),offset,offset+length,android.text.Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);offset+=length;
        }
        view.setText(span);
    }
    private static final class RunSpan extends android.text.style.MetricAffectingSpan {
        final float size;final int color,decoration;final android.graphics.Typeface face;final String axes;
        RunSpan(float size,int color,android.graphics.Typeface face,int decoration,String axes){this.size=size;this.color=color;this.face=face;this.decoration=decoration;this.axes=axes;}
        private void apply(android.text.TextPaint p){p.setTextSize(size);p.setTypeface(face);if(!axes.isEmpty())p.setFontVariationSettings(axes);p.setColor(color);p.setUnderlineText((decoration&1)!=0);p.setStrikeThruText((decoration&2)!=0);}
        @Override public void updateMeasureState(android.text.TextPaint p){apply(p);}
        @Override public void updateDrawState(android.text.TextPaint p){apply(p);}
    }
    private static com.google.android.material.bottomnavigation.BottomNavigationView newBottomNavigation(android.content.Context context) {
        return new com.google.android.material.bottomnavigation.BottomNavigationView(new android.view.ContextThemeWrapper(context, com.google.android.material.R.style.Theme_MaterialComponents_DayNight_NoActionBar));
    }
    public void bottomNavigation(View view, int selected, String[] labels, int[] icons) {
        NodeView n = (NodeView)view;
        com.google.android.material.bottomnavigation.BottomNavigationView bar = (com.google.android.material.bottomnavigation.BottomNavigationView)n.content;
        n.updating = true;
        try {
            if (!java.util.Arrays.equals(labels, n.tabLabels) || !java.util.Arrays.equals(icons, n.tabIcons)) {
                int[] resources = {android.R.drawable.ic_menu_view, android.R.drawable.ic_menu_search, android.R.drawable.btn_star, android.R.drawable.ic_menu_myplaces, android.R.drawable.ic_menu_preferences};
                bar.getMenu().clear();
                for (int i = 0; i < labels.length; i++) bar.getMenu().add(0, i + 1, i, labels[i]).setIcon(resources[icons[i]]).setEnabled(n.isEnabled());
                n.tabLabels = labels.clone(); n.tabIcons = icons.clone();
            }
            bar.setSelectedItemId(selected + 1);
        } finally { n.updating = false; }
    }
    public void enabled(View view,boolean enabled){NodeView n=(NodeView)view;n.setEnabled(enabled);if(n.content!=null)n.content.setEnabled(enabled);if(n.kind==14){android.view.Menu menu=((com.google.android.material.bottomnavigation.BottomNavigationView)n.content).getMenu();for(int i=0;i<menu.size();i++)menu.getItem(i).setEnabled(enabled);}}
    public void decoration(View view,float[] values,int[] colors,int[] styles){
        NodeView n=(NodeView)view;for(int i=0;i<4;i++){n.cornerRadii[i]=(float)Math.min(Float.MAX_VALUE,(double)values[i]*density);n.borderWidths[i]=(float)Math.min(Float.MAX_VALUE,(double)values[i+4]*density);}
        n.borderColors=colors;n.borderStyles=styles;applyBox(n);
    }
    private static final class BoxShape {
        final float[] radii,widths;final android.graphics.Path outer,ring,center;final android.graphics.Path[] wedges;
        BoxShape(NodeView n,android.graphics.RectF box){
            float w=box.width(),h=box.height();radii=n.cornerRadii.clone();widths=n.borderWidths.clone();
            double factor=1;int[][] edges={{0,1},{3,2},{0,3},{1,2}};
            for(int i=0;i<4;i++){double sum=(double)radii[edges[i][0]]+radii[edges[i][1]];if(sum>0)factor=Math.min(factor,(i<2?w:h)/sum);}
            for(int i=0;i<4;i++)radii[i]*=Math.max(0,factor);
            for(int axis=0;axis<2;axis++){double sum=(double)widths[axis]+widths[axis+2];float limit=axis==0?w:h;if(sum>limit&&sum>0){double f=Math.max(0,limit)/sum;widths[axis]*=f;widths[axis+2]*=f;}}
            float l=widths[0],t=widths[1],r=widths[2],b=widths[3],x=box.left,y=box.top;
            outer=inset(box,radii,0,0,0,0);ring=new android.graphics.Path(outer);ring.addPath(inset(box,radii,l,t,r,b));ring.setFillType(android.graphics.Path.FillType.EVEN_ODD);
            center=inset(box,radii,l/2,t/2,r/2,b/2);
            // Side ownership must extend through the rounded corners into the interior.
            // Stopping at the inner border rectangle cuts the curved parts out of the ring.
            double reach=Math.min(l+r>0?w/((double)l+r):Double.POSITIVE_INFINITY,t+b>0?h/((double)t+b):Double.POSITIVE_INFINITY);
            if(!Double.isFinite(reach))reach=1;
            float il=x+(float)(l*reach),it=y+(float)(t*reach),ir=x+w-(float)(r*reach),ib=y+h-(float)(b*reach);
            wedges=new android.graphics.Path[]{polygon(x,y,il,it,il,ib,x,y+h),polygon(x,y,x+w,y,ir,it,il,it),polygon(x+w,y,x+w,y+h,ir,ib,ir,it),polygon(x,y+h,il,ib,ir,ib,x+w,y+h)};
        }
        static android.graphics.Path polygon(float... p){android.graphics.Path path=new android.graphics.Path();path.moveTo(p[0],p[1]);for(int i=2;i<p.length;i+=2)path.lineTo(p[i],p[i+1]);path.close();return path;}
        static android.graphics.Path inset(android.graphics.RectF box,float[] r,float l,float t,float right,float b){
            android.graphics.Path path=new android.graphics.Path();float w=box.width()-l-right,h=box.height()-t-b;if(w<=0||h<=0)return path;
            float[] rr={Math.max(0,r[0]-l),Math.max(0,r[0]-t),Math.max(0,r[1]-right),Math.max(0,r[1]-t),Math.max(0,r[2]-right),Math.max(0,r[2]-b),Math.max(0,r[3]-l),Math.max(0,r[3]-b)};
            double factor=1;int[][] sums={{0,2},{6,4},{1,7},{3,5}};
            for(int i=0;i<4;i++){double sum=(double)rr[sums[i][0]]+rr[sums[i][1]];if(sum>0)factor=Math.min(factor,(i<2?w:h)/sum);}
            for(int i=0;i<8;i++)rr[i]*=factor;
            path.addRoundRect(new android.graphics.RectF(box.left+l,box.top+t,box.right-right,box.bottom-b),rr,android.graphics.Path.Direction.CW);return path;
        }
    }
    private static void drawBorders(android.graphics.Canvas canvas,NodeView node,BoxShape shape){
        android.graphics.Paint paint=new android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG);
        boolean uniformSolid=true;
        for(int i=0;i<4;i++)uniformSolid &= node.borderStyles[i]==0 && node.borderColors[i]==node.borderColors[0];
        if(uniformSolid){
            paint.setColor(node.borderColors[0]);canvas.drawPath(shape.ring,paint);return;
        }
        for(int i=0;i<4;i++){float width=shape.widths[i];if(width<=0 || android.graphics.Color.alpha(node.borderColors[i])==0)continue;
            int save=canvas.save();canvas.clipPath(shape.ring);canvas.clipPath(shape.wedges[i]);paint.setColor(node.borderColors[i]);
            if(node.borderStyles[i]==0){paint.setStyle(android.graphics.Paint.Style.FILL);paint.setPathEffect(null);canvas.drawPaint(paint);}
            else {paint.setStyle(android.graphics.Paint.Style.STROKE);paint.setStrokeWidth(width);paint.setStrokeCap(node.borderStyles[i]==2?android.graphics.Paint.Cap.ROUND:android.graphics.Paint.Cap.BUTT);paint.setPathEffect(new android.graphics.DashPathEffect(new float[]{node.borderStyles[i]==1?3*width:0.001f*width,2*width},0));canvas.drawPath(shape.center,paint);}
            canvas.restoreToCount(save);
        }
    }
    private void applyBox(NodeView n){
        android.graphics.drawable.Drawable box=new BoxDrawable(n);
        n.setBackground(n.kind==3?new android.graphics.drawable.RippleDrawable(android.content.res.ColorStateList.valueOf(0x33000000),box,null):box);
        n.setLayerType(View.LAYER_TYPE_NONE,null);
        n.setClipToOutline(false);n.invalidateOutline();
    }
    private static final class BoxDrawable extends android.graphics.drawable.Drawable {
        final NodeView node;
        final android.graphics.Paint paint=new android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG);
        android.graphics.Bitmap shadow;
        android.graphics.RectF shadowBounds;
        BoxDrawable(NodeView node){this.node=node;}
        @Override protected void onBoundsChange(android.graphics.Rect bounds){shadow=null;shadowBounds=null;}
        private void drawShadow(android.graphics.Canvas canvas,android.graphics.RectF r){
            if(node.clip || (node.shadowColor>>>24)==0 || r.width()<=0 || r.height()<=0)return;
            if(shadow==null){
                // Rasterize only the shadow in a bounded software surface; keep native controls accelerated.
                float pad=node.shadowBlur*3+Math.max(Math.abs(node.shadowX),Math.abs(node.shadowY))+2;
                shadowBounds=new android.graphics.RectF(r.left-pad,r.top-pad,r.right+pad,r.bottom+pad);
                float scale=Math.min(1f,2048f/Math.max(shadowBounds.width(),shadowBounds.height()));
                int w=Math.max(1,Math.round(shadowBounds.width()*scale)),h=Math.max(1,Math.round(shadowBounds.height()*scale));
                shadow=android.graphics.Bitmap.createBitmap(w,h,android.graphics.Bitmap.Config.ARGB_8888);
                android.graphics.Canvas software=new android.graphics.Canvas(shadow);
                software.scale(scale,scale);software.translate(-shadowBounds.left,-shadowBounds.top);
                android.graphics.Paint p=new android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG);
                p.setColor(android.graphics.Color.BLACK);
                p.setShadowLayer(Math.max(0.001f,node.shadowBlur),node.shadowX,node.shadowY,node.shadowColor);
                software.drawPath(new BoxShape(node,r).outer,p);
                p.clearShadowLayer();p.setXfermode(new android.graphics.PorterDuffXfermode(android.graphics.PorterDuff.Mode.CLEAR));
                software.drawPath(new BoxShape(node,r).outer,p);
            }
            canvas.drawBitmap(shadow,null,shadowBounds,null);
        }
        @Override public void draw(android.graphics.Canvas canvas){
            android.graphics.RectF r=new android.graphics.RectF(getBounds());
            drawShadow(canvas,r);
            paint.setStyle(android.graphics.Paint.Style.FILL);paint.setColor(node.boxColor);
            canvas.drawPath(new BoxShape(node,r).outer,paint);
            if(node.gradientKind!=0 && r.width()>0 && r.height()>0){
                float sx=r.left+node.gradientSx*r.width(),sy=r.top+node.gradientSy*r.height();
                android.graphics.Shader shader;
                if(node.gradientKind==2)shader=new android.graphics.RadialGradient(sx,sy,Math.max(0.001f,node.gradientEx*Math.min(r.width(),r.height())),node.gradientColors,node.gradientOffsets,android.graphics.Shader.TileMode.CLAMP);
                else if(node.gradientKind==3){
                    shader=new android.graphics.SweepGradient(sx,sy,node.gradientColors,node.gradientOffsets);
                    android.graphics.Matrix matrix=new android.graphics.Matrix();matrix.setRotate(-90,sx,sy);shader.setLocalMatrix(matrix);
                }else shader=new android.graphics.LinearGradient(sx,sy,r.left+node.gradientEx*r.width(),r.top+node.gradientEy*r.height(),node.gradientColors,node.gradientOffsets,android.graphics.Shader.TileMode.CLAMP);
                paint.setColor(android.graphics.Color.WHITE);paint.setShader(shader);canvas.drawPath(new BoxShape(node,r).outer,paint);paint.setShader(null);
            }
            if(node.backgroundBitmap!=null && r.width()>0 && r.height()>0){
                float w=node.backgroundBitmap.getWidth(),h=node.backgroundBitmap.getHeight();
                float contain=Math.min(r.width()/w,r.height()/h),scale=contain;
                if(node.backgroundFit==1)scale=Math.max(r.width()/w,r.height()/h);
                else if(node.backgroundFit==3)scale=1;else if(node.backgroundFit==4)scale=Math.min(1,contain);
                float dw=node.backgroundFit==2?r.width():w*scale,dh=node.backgroundFit==2?r.height():h*scale;
                android.graphics.RectF dst=new android.graphics.RectF(r.centerX()-dw/2,r.centerY()-dh/2,r.centerX()+dw/2,r.centerY()+dh/2);
                android.graphics.Path clip=new BoxShape(node,r).outer;
                int save=canvas.save();canvas.clipPath(clip);paint.setColor(android.graphics.Color.WHITE);paint.setFilterBitmap(true);
                canvas.drawBitmap(node.backgroundBitmap,null,dst,paint);canvas.restoreToCount(save);
            }

        }
        @Override public void getOutline(android.graphics.Outline outline){if(getBounds().width()>0&&getBounds().height()>0)outline.setConvexPath(new BoxShape(node,new android.graphics.RectF(getBounds())).outer);else outline.setEmpty();}
        @Override public void setAlpha(int alpha){paint.setAlpha(alpha);}
        @Override public void setColorFilter(android.graphics.ColorFilter filter){paint.setColorFilter(filter);}
        @Override public int getOpacity(){return android.graphics.PixelFormat.TRANSLUCENT;}
    }

    private static void configureInput(EditText field,int keyboard,int action,int capitalization,int autofill,int flags){
        int start=field.getSelectionStart(),end=field.getSelectionEnd();
        boolean password=(flags&1)!=0,multiline=(flags&2)!=0,correct=(flags&4)!=0 && !password;
        int type=android.text.InputType.TYPE_CLASS_TEXT;
        if(password)type|=android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD;
        else switch(keyboard){
            case 1:type|=android.text.InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS;break;
            case 2:type=android.text.InputType.TYPE_CLASS_NUMBER;break;
            case 3:type=android.text.InputType.TYPE_CLASS_NUMBER|android.text.InputType.TYPE_NUMBER_FLAG_DECIMAL;break;
            case 4:type=android.text.InputType.TYPE_CLASS_PHONE;break;
            case 5:type|=android.text.InputType.TYPE_TEXT_VARIATION_URI;break;
        }
        if((type & android.text.InputType.TYPE_MASK_CLASS)==android.text.InputType.TYPE_CLASS_TEXT){
            if(multiline)type|=android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE;
            if(correct)type|=android.text.InputType.TYPE_TEXT_FLAG_AUTO_CORRECT;
            else type|=android.text.InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS;
            if(!password)type|=new int[]{0,android.text.InputType.TYPE_TEXT_FLAG_CAP_SENTENCES,android.text.InputType.TYPE_TEXT_FLAG_CAP_WORDS,android.text.InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS}[capitalization];
        }
        if(field.getInputType()!=type){
            android.graphics.Typeface font=field.getTypeface();
            field.setInputType(type);field.setTypeface(font);
        }
        field.setSingleLine(!multiline);
        field.setTransformationMethod(password?android.text.method.PasswordTransformationMethod.getInstance():(multiline?null:android.text.method.SingleLineTransformationMethod.getInstance()));
        field.setGravity(multiline?android.view.Gravity.TOP:android.view.Gravity.CENTER_VERTICAL);
        int ime=new int[]{0,6,5,3,4,2}[action];
        field.setImeOptions(ime|android.view.inputmethod.EditorInfo.IME_FLAG_NO_EXTRACT_UI|(multiline&&action==0?android.view.inputmethod.EditorInfo.IME_FLAG_NO_ENTER_ACTION:0));
        field.setAutofillHints(autofill==0?null:new String[]{new String[]{"","username","password","newPassword","emailAddress","smsOTPCode"}[autofill]});
        if(start>=0 && end>=0)field.setSelection(Math.min(start,field.length()),Math.min(end,field.length()));
    }
    public void textInput(View view,int keyboard,int action,int capitalization,int autofill,int flags){
        NodeView n=(NodeView)view;n.updating=true;
        try{configureInput((EditText)n.content,keyboard,action,capitalization,autofill,flags);}
        finally{n.updating=false;}
    }
    public void hintColor(View view,int color){((EditText)((NodeView)view).content).setHintTextColor(color);}
    public void control(View view,float value,float minimum,float maximum,int revision,boolean focus,String placeholder){
        NodeView n=(NodeView)view;n.updating=true;
        try{
            if(n.content instanceof android.widget.RadioButton)((android.widget.RadioButton)n.content).setChecked(value==1);
            if(n.content instanceof android.widget.Switch)((android.widget.Switch)n.content).setChecked(value!=0);
            if(n.content instanceof SeekBar){n.minimum=minimum;n.maximum=maximum;((SeekBar)n.content).setProgress(Math.round((value-minimum)/(maximum-minimum)*10000));}
            if(n.content instanceof EditText){
                EditText field=(EditText)n.content;field.setHint(placeholder);
                if(revision!=0 && revision!=n.focusRevision){n.focusRevision=revision;field.post(()->{
                    android.view.inputmethod.InputMethodManager keyboard=(android.view.inputmethod.InputMethodManager)getSystemService(INPUT_METHOD_SERVICE);
                    if(focus){field.requestFocus();keyboard.showSoftInput(field,android.view.inputmethod.InputMethodManager.SHOW_IMPLICIT);}
                    else{field.clearFocus();keyboard.hideSoftInputFromWindow(field.getWindowToken(),0);}
                });}
            }
        }finally{n.updating=false;}
    }
    private final android.util.LruCache<String,android.graphics.Bitmap> decodedImages = new android.util.LruCache<String,android.graphics.Bitmap>(32*1024*1024){
        @Override protected int sizeOf(String key,android.graphics.Bitmap bitmap){return bitmap.getAllocationByteCount();}
    };
    private static int imageOrientation(byte[] data){
        try{return new android.media.ExifInterface(new java.io.ByteArrayInputStream(data)).getAttributeInt(android.media.ExifInterface.TAG_ORIENTATION,1);}
        catch(java.io.IOException ignored){return 1;}
    }
    private android.graphics.Bitmap decodeImage(byte[] data){
        final String key;
        try{key=android.util.Base64.encodeToString(java.security.MessageDigest.getInstance("SHA-256").digest(data),android.util.Base64.NO_WRAP);}
        catch(java.security.NoSuchAlgorithmException error){throw new IllegalStateException(error);}
        android.graphics.Bitmap bitmap=decodedImages.get(key);
        if(bitmap==null){
            bitmap=android.graphics.BitmapFactory.decodeByteArray(data,0,data.length);
            if(bitmap==null)throw new IllegalArgumentException("Invalid encoded image");
            int orientation=imageOrientation(data);
            android.graphics.Matrix transform=new android.graphics.Matrix();
            switch(orientation){
                case 2:transform.setScale(-1,1);break;
                case 3:transform.setRotate(180);break;
                case 4:transform.setScale(1,-1);break;
                case 5:transform.setRotate(90);transform.postScale(-1,1);break;
                case 6:transform.setRotate(90);break;
                case 7:transform.setRotate(-90);transform.postScale(-1,1);break;
                case 8:transform.setRotate(-90);break;
            }
            if(!transform.isIdentity()){
                android.graphics.Bitmap upright=android.graphics.Bitmap.createBitmap(bitmap,0,0,bitmap.getWidth(),bitmap.getHeight(),transform,true);
                if(upright!=bitmap)bitmap.recycle();
                bitmap=upright;
            }
            decodedImages.put(key,bitmap);
        }
        return bitmap;
    }
    public void backgroundImage(View view,byte[] data,int fit){
        NodeView node=(NodeView)view;
        node.backgroundBitmap=data.length==0?null:decodeImage(data);
        if(data.length!=0 && node.backgroundBitmap==null)throw new IllegalArgumentException("Invalid background image");
        node.backgroundFit=fit;applyBox(node);
    }
    public void imageTint(View view,boolean enabled,int color){
        ImageView image=(ImageView)((NodeView)view).content;
        if(enabled)image.setColorFilter(color,android.graphics.PorterDuff.Mode.SRC_IN);else image.clearColorFilter();
    }
    public void imageFit(View view,int fit){
        ImageView image=(ImageView)((NodeView)view).content;
        image.setScaleType(new ImageView.ScaleType[]{ImageView.ScaleType.FIT_CENTER,ImageView.ScaleType.CENTER_CROP,ImageView.ScaleType.FIT_XY,ImageView.ScaleType.CENTER,ImageView.ScaleType.CENTER_INSIDE}[fit]);
    }
    public void image(View view,byte[] data){
        android.graphics.Bitmap image=decodeImage(data);
        if(image==null)throw new IllegalArgumentException("Invalid encoded image");
        ((ImageView)((NodeView)view).content).setImageBitmap(image);
    }
    public void frame(View view,float x,float y,float w,float h){
        NodeView n=(NodeView)view;n.x=x*density;n.y=y*density;n.w=w*density;n.h=h*density;
        n.requestLayout();if(n.getParent()!=null)((View)n.getParent()).requestLayout();
    }
    private void navigationBack(NodeView page) {
        for (int i = 0; i < page.getChildCount(); i++) {
            View child = page.getChildAt(i);
            if (child instanceof NodeView) {
                NodeView node = (NodeView)child;
                if (node.kind == 13) { updateBack(node.nodeId, node.canPop); return; }
                navigationBack(node);
            }
        }
    }

    public void navigation(View view, View[] pages, int depth, int kind, float seconds, int easing) {
        NodeView nav = (NodeView)view;
        NodeView to = (NodeView)pages[depth - 1];
        if (nav.navigationBusy) return;
        nav.navigationPages = pages;
        nav.navigationKind = kind; nav.navigationSeconds = seconds; nav.navigationEasing = easing;
        backNavigation = nav;
        for (View page : pages) page.setVisibility(page == nav.navigationPage ? View.VISIBLE : View.INVISIBLE);
        if (nav.navigationPage == to) { navigationBack(to); return; }
        NodeView from = nav.navigationPage;
        boolean forward = depth > nav.navigationDepth;
        nav.navigationPage = to;
        nav.navigationDepth = depth;
        nav.setClipChildren(true);
        nav.setClipToPadding(true);
        to.setVisibility(View.VISIBLE);
        navigationBack(to);
        if (from == null || kind == 3 || !android.animation.ValueAnimator.areAnimatorsEnabled()) {
            if (from != null) from.setVisibility(View.INVISIBLE);
            event(nav.nodeId, 5, depth, "");
            return;
        }
        nav.navigationBusy = true;
        nav.clearFocus();
        nav.setDescendantFocusability(ViewGroup.FOCUS_BLOCK_DESCENDANTS);
        event(nav.nodeId, 6, 0, "");
        // A hardware layer caches each retained page while RenderThread animates it.
        // Keep the outgoing page opaque during a fade, so the host cannot flash through.
        if (forward || kind == 2) to.bringToFront(); else from.bringToFront();
        float width = nav.w;
        float incomingX = kind == 2 ? 0 : forward ? width : -.25f * width;
        float outgoingX = kind == 2 ? 0 : forward ? -.25f * width : width;
        to.setTranslationX(incomingX);
        to.setAlpha(kind == 2 ? 0 : 1);
        from.setLayerType(View.LAYER_TYPE_HARDWARE, null);
        to.setLayerType(View.LAYER_TYPE_HARDWARE, null);
        android.animation.AnimatorSet animation = new android.animation.AnimatorSet();
        animation.playTogether(
            android.animation.ObjectAnimator.ofFloat(from, View.TRANSLATION_X, 0, outgoingX),
            android.animation.ObjectAnimator.ofFloat(to, View.TRANSLATION_X, incomingX, 0),
            android.animation.ObjectAnimator.ofFloat(to, View.ALPHA, kind == 2 ? 0 : 1, 1));
        animation.setDuration(Math.max(1, Math.round(seconds * 1000)));
        animation.setInterpolator(t -> easing == 0 ? t : easing == 2 ? 1-(1-t)*(1-t)*(1-t) : easing == 3 ? t*t*t : t*t*(3-2*t));
        animation.addListener(new android.animation.AnimatorListenerAdapter() {
            @Override public void onAnimationEnd(android.animation.Animator animator) {
                from.setVisibility(View.INVISIBLE);
                from.setTranslationX(0); from.setAlpha(1);
                to.setTranslationX(0); to.setAlpha(1);
                from.setLayerType(View.LAYER_TYPE_NONE, null);
                to.setLayerType(View.LAYER_TYPE_NONE, null);
                nav.navigationBusy = false;
                nav.setDescendantFocusability(ViewGroup.FOCUS_BEFORE_DESCENDANTS);
                nav.navigationAnimation = null;
                // A reactive reparent can detach/re-attach within one native batch.
                // Finish after that batch; a destroyed session stays detached.
                nav.post(() -> { if (nav.isAttachedToWindow()) event(nav.nodeId, 5, depth, ""); });
            }
        });
        nav.navigationAnimation = animation;
        animation.start();
    }

    private NodeView backNavigation;
    private int backNodeId;
    private boolean backRegistered;
    private Object backCallback;

    private void updateBack(int id, boolean canPop) {
        backNodeId = canPop ? id : 0;
        if (android.os.Build.VERSION.SDK_INT >= 33) BackApi33.update(this, canPop);
    }

    // Keep API 33 types out of fields and initialization on Android 30–32.
    private static final class BackApi33 {
        static void update(NativeHost host, boolean canPop) {
            if (canPop && !host.backRegistered) {
                android.window.OnBackInvokedCallback callback = android.os.Build.VERSION.SDK_INT >= 34
                    ? BackApi34.create(host) : () -> {
                        if (host.backNodeId != 0) event(host.backNodeId, 3, -1, "");
                    };
                host.getOnBackInvokedDispatcher().registerOnBackInvokedCallback(android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT, callback);
                host.backCallback = callback;
                host.backRegistered = true;
            } else if (!canPop && host.backRegistered) {
                host.getOnBackInvokedDispatcher().unregisterOnBackInvokedCallback((android.window.OnBackInvokedCallback)host.backCallback);
                host.backCallback = null;
                host.backRegistered = false;
            }
        }
    }

    // Kept in its own class so Android 30–33 never resolve API 34 BackEvent types.
    private static final class BackApi34 {
        static android.window.OnBackInvokedCallback create(NativeHost host) {
            return new android.window.OnBackAnimationCallback() {
                private NodeView nav;
                @Override public void onBackStarted(android.window.BackEvent event) {
                    nav = host.backNavigation;
                    if (nav == null || nav.navigationBusy || nav.navigationDepth < 2) { nav = null; return; }
                    nav.predictiveFrom = nav.navigationPage;
                    nav.predictiveTo = (NodeView)nav.navigationPages[nav.navigationDepth - 2];
                    nav.predictiveDirection = event.getSwipeEdge() == android.window.BackEvent.EDGE_RIGHT ? -1 : 1;
                    nav.predictiveProgress = 0;
                    nav.navigationBusy = true;
                    nav.clearFocus();
                    nav.setDescendantFocusability(ViewGroup.FOCUS_BLOCK_DESCENDANTS);
                    nav.predictiveTo.setVisibility(View.VISIBLE);
                    if (nav.navigationKind == 2) nav.predictiveTo.bringToFront(); else nav.predictiveFrom.bringToFront();
                    nav.predictiveFrom.setLayerType(View.LAYER_TYPE_HARDWARE, null);
                    nav.predictiveTo.setLayerType(View.LAYER_TYPE_HARDWARE, null);
                    host.predictiveProgress(nav, 0);
                    NativeHost.event(nav.nodeId, 6, 0, "");
                }
                @Override public void onBackProgressed(android.window.BackEvent event) {
                    if (nav != null) host.predictiveProgress(nav, event.getProgress());
                }
                @Override public void onBackCancelled() {
                    if (nav != null) host.finishPredictive(nav, false);
                    nav = null;
                }
                @Override public void onBackInvoked() {
                    if (nav != null) host.finishPredictive(nav, true);
                    else if (host.backNodeId != 0 && (host.backNavigation == null || !host.backNavigation.navigationBusy))
                        NativeHost.event(host.backNodeId, 3, -1, "");
                    nav = null;
                }
            };
        }
    }

    private void predictiveProgress(NodeView nav, float value) {
        if (nav.predictiveFrom == null) return;
        float p = Math.max(0, Math.min(1, value));
        nav.predictiveProgress = p;
        // The gesture follows the finger linearly; the configured curve is used
        // only for the settle animation after release/cancel.
        if (nav.navigationKind == 3 || !android.animation.ValueAnimator.areAnimatorsEnabled()) {
            nav.predictiveTo.setAlpha(0);
        } else if (nav.navigationKind == 2) {
            nav.predictiveTo.setAlpha(p);
        } else {
            nav.predictiveFrom.setTranslationX(p * nav.w * nav.predictiveDirection);
            nav.predictiveTo.setTranslationX(-.25f * (1-p) * nav.w * nav.predictiveDirection);
        }
    }

    private void finishPredictive(NodeView nav, boolean commit) {
        if (nav.predictiveFrom == null || nav.navigationAnimation != null) return;
        float start = nav.predictiveProgress;
        android.animation.ValueAnimator animation = android.animation.ValueAnimator.ofFloat(start, commit ? 1 : 0);
        animation.setDuration(nav.navigationKind == 3 || !android.animation.ValueAnimator.areAnimatorsEnabled() ? 0
            : Math.max(1, Math.round(nav.navigationSeconds * 1000 * (commit ? 1-start : start))));
        int easing = nav.navigationEasing;
        animation.setInterpolator(t -> easing == 0 ? t : easing == 2 ? 1-(1-t)*(1-t)*(1-t) : easing == 3 ? t*t*t : t*t*(3-2*t));
        animation.addUpdateListener(value -> predictiveProgress(nav, (float)value.getAnimatedValue()));
        animation.addListener(new android.animation.AnimatorListenerAdapter() {
            @Override public void onAnimationEnd(android.animation.Animator animator) {
                NodeView from = nav.predictiveFrom, to = nav.predictiveTo;
                if (commit) { nav.navigationPage = to; nav.navigationDepth--; }
                from.setVisibility(commit ? View.INVISIBLE : View.VISIBLE);
                to.setVisibility(commit ? View.VISIBLE : View.INVISIBLE);
                from.setTranslationX(0); to.setTranslationX(0); from.setAlpha(1); to.setAlpha(1);
                from.setLayerType(View.LAYER_TYPE_NONE, null); to.setLayerType(View.LAYER_TYPE_NONE, null);
                nav.navigationPage.bringToFront();
                navigationBack(nav.navigationPage);
                nav.navigationBusy = false;
                nav.navigationAnimation = null;
                nav.predictiveFrom = null; nav.predictiveTo = null;
                nav.setDescendantFocusability(ViewGroup.FOCUS_BEFORE_DESCENDANTS);
                int depth = nav.navigationDepth;
                nav.post(() -> { if (nav.isAttachedToWindow()) event(nav.nodeId, 5, depth, ""); });
            }
        });
        nav.navigationAnimation = animation;
        animation.start();
    }

    @Override public void onBackPressed() {
        if (backNodeId != 0) event(backNodeId, 3, -1, "");
        else super.onBackPressed();
    }

    public void header(View view, String title, boolean back, boolean canPop, int leading, String[] labels, int foreground) {
        NodeView node = (NodeView)view;
        node.canPop = canPop;
        if (node.isShown()) updateBack(node.nodeId, canPop);
        android.widget.Toolbar toolbar = (android.widget.Toolbar)node.content;
        toolbar.setTitle(title);
        toolbar.setTitleTextColor(foreground);
        toolbar.setNavigationIcon(null);
        if (back) {
            android.util.TypedValue icon = new android.util.TypedValue();
            if (getTheme().resolveAttribute(android.R.attr.homeAsUpIndicator, icon, true) && icon.resourceId != 0) {
                android.graphics.drawable.Drawable drawable = getDrawable(icon.resourceId).mutate();
                drawable.setTint(foreground);
                toolbar.setNavigationIcon(drawable);
            }
        }
        toolbar.setNavigationContentDescription("Back");
        toolbar.setNavigationOnClickListener(v -> event(node.nodeId, 3, -1, ""));
        toolbar.getMenu().clear();
        for (int i = toolbar.getChildCount() - 1; i >= 0; i--) {
            View child = toolbar.getChildAt(i);
            if ("DotNativeLeading".equals(child.getTag())) toolbar.removeViewAt(i);
        }
        android.widget.LinearLayout start = new android.widget.LinearLayout(this);
        start.setTag("DotNativeLeading");
        for (int i = 0; i < labels.length; i++) {
            final int index = i;
            if (i < leading) {
                Button button = new Button(this);
                button.setText(labels[i]); button.setTextColor(foreground); button.setAllCaps(false);
                android.util.TypedValue ripple = new android.util.TypedValue();
                if(getTheme().resolveAttribute(android.R.attr.selectableItemBackgroundBorderless, ripple, true)) button.setBackgroundResource(ripple.resourceId);
                button.setMinimumWidth(0); button.setMinWidth(0);
                button.setPadding(Math.round(12*density),0,Math.round(12*density),0);
                button.setOnClickListener(v -> event(node.nodeId, 3, index, ""));
                start.addView(button);
            } else {
                android.text.SpannableString label = new android.text.SpannableString(labels[i]);
                label.setSpan(new android.text.style.ForegroundColorSpan(foreground), 0, label.length(), 0);
                toolbar.getMenu().add(0, i + 1, i, label).setShowAsAction(android.view.MenuItem.SHOW_AS_ACTION_IF_ROOM);
            }
        }
        if (leading > 0) toolbar.addView(start, new android.widget.Toolbar.LayoutParams(-2, -1, android.view.Gravity.START));
        toolbar.setOnMenuItemClickListener(item -> { event(node.nodeId, 3, item.getItemId() - 1, ""); return true; });
    }

    public float[] measure(int kind,String text,float font,float width,byte[] data,String family,int weight,boolean italic,int alignment,int maxLines,int overflow,float letterSpacing,float lineHeight,int decoration,byte[] runs,String axes,int keyboard,int action,int capitalization,int autofill,int flags){
        View v=measureControls.get(kind);
        if(v==null) switch(kind){
            case 2:v=measureText;break;case 3:v=measureButton;break;
            case 5:v=new EditText(this);break;case 6:v=new android.widget.Switch(this);break;
            case 7:v=new SeekBar(this);break;
            case 12:v=new android.widget.RadioButton(this);break;
            case 13:return new float[]{Math.max(0,width),56};
            case 14:v=newBottomNavigation(this);break;
            case 8:{android.graphics.BitmapFactory.Options bounds=new android.graphics.BitmapFactory.Options();bounds.inJustDecodeBounds=true;android.graphics.BitmapFactory.decodeByteArray(data,0,data.length,bounds);if(bounds.outWidth<=0||bounds.outHeight<=0)throw new IllegalArgumentException("Invalid encoded image");int orientation=imageOrientation(data);return orientation>=5&&orientation<=8?new float[]{bounds.outHeight/density,bounds.outWidth/density}:new float[]{bounds.outWidth/density,bounds.outHeight/density};}
            case 9:{android.graphics.drawable.Drawable icon=getDrawable(icon(text));return new float[]{icon.getIntrinsicWidth()/density,icon.getIntrinsicHeight()/density};}
            case 16:return new float[]{0,0};
            default:throw new IllegalArgumentException("Container requested leaf measure");
        }
        if(measureControls.get(kind)==null){
            measureControls.put(kind,v);
            v.setLayoutParams(new ViewGroup.LayoutParams(-2,-2));
            v.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        }
        if(v instanceof TextView){textStyle((TextView)v,text,font,0xff000000);applyTypography((TextView)v,kind,family,weight,italic,alignment,maxLines,overflow,letterSpacing,lineHeight,decoration);}
        if(v instanceof TextView)((TextView)v).setFontVariationSettings(axes.isEmpty()?null:axes);
        if(kind==2)applyRuns((TextView)v,runs,axes);
        if(kind==5)configureInput((EditText)v,keyboard,action,capitalization,autofill,flags);
        if(kind==5 || kind==7)v.setMinimumWidth(Math.round(120*density));
        int spec=width<0?View.MeasureSpec.makeMeasureSpec(0,View.MeasureSpec.UNSPECIFIED):View.MeasureSpec.makeMeasureSpec(Math.max(0,Math.round(width*density)),View.MeasureSpec.AT_MOST);
        v.forceLayout();
        v.measure(spec,View.MeasureSpec.makeMeasureSpec(0,View.MeasureSpec.UNSPECIFIED));
        return new float[]{v.getMeasuredWidth()/density,v.getMeasuredHeight()/density};
    }
    public void scrollPosition(View view,int revision,float offset,boolean observe){
        NodeView node=(NodeView)view;
        boolean changed=node.scrollRevision!=revision;
        node.scrollRevision=revision;node.observeScroll=observe;
        if(changed){node.requestedScroll=Math.round(offset*density);node.pendingScroll=true;}
        node.requestLayout();
    }
    private void observeScroll(NodeView node){
        if(!node.touchObserved){
            node.touchObserved=true;
            android.view.GestureDetector tap=new android.view.GestureDetector(this,new android.view.GestureDetector.SimpleOnGestureListener(){
                @Override public boolean onSingleTapUp(MotionEvent event){NativeHost.event(node.nodeId,12,event.getY()/density,"");return false;}
            });
            node.content.setOnTouchListener((view,event)->{tap.onTouchEvent(event);return false;});
        }
        node.content.setOnScrollChangeListener((v,x,y,oldX,oldY)->node.reportScroll());
    }
    public void scrollPolicy(View view, boolean contain, int axis) {
        NodeView node = (NodeView)view;
        if(node.content instanceof NativeCollection){((NativeCollection)node.content).setContain(contain);return;}
        boolean horizontal = axis == 1;
        if ((node.content instanceof NativeHorizontalScroll) != horizontal) {
            ((ViewGroup)node.content).removeView(node.document);
            node.removeView(node.content);
            node.content = horizontal ? new NativeHorizontalScroll(this) : new NativeScroll(this);
            ((ViewGroup)node.content).addView(node.document);
            node.addView(node.content);
            observeScroll(node);
            node.requestLayout();
        }
        if (horizontal) ((NativeHorizontalScroll)node.content).setContain(contain);
        else ((NativeScroll)node.content).setContain(contain);
    }

    private static final class NativeHorizontalScroll extends android.widget.HorizontalScrollView {
        private boolean contain;
        private boolean touching;
        NativeHorizontalScroll(android.content.Context context) { super(context); }

        void setContain(boolean value) {
            contain = value;
            if (touching && getParent() != null) getParent().requestDisallowInterceptTouchEvent(value);
        }

        @Override public boolean dispatchTouchEvent(MotionEvent event) {
            int action = event.getActionMasked();
            if (action == MotionEvent.ACTION_DOWN) {
                touching = true;
                if (contain && getParent() != null) getParent().requestDisallowInterceptTouchEvent(true);
            }
            try { return super.dispatchTouchEvent(event); }
            finally {
                if (action == MotionEvent.ACTION_UP || action == MotionEvent.ACTION_CANCEL) {
                    touching = false;
                    if (contain && getParent() != null) getParent().requestDisallowInterceptTouchEvent(false);
                }
            }
        }
    }

    private static final class NativeScroll extends androidx.core.widget.NestedScrollView {
        private boolean contain;
        private boolean touching;
        NativeScroll(android.content.Context context) { super(context); }

        void setContain(boolean value) {
            contain = value;
            if (value) {
                stopNestedScroll(androidx.core.view.ViewCompat.TYPE_TOUCH);
                stopNestedScroll(androidx.core.view.ViewCompat.TYPE_NON_TOUCH);
            }
            setNestedScrollingEnabled(!value);
            if (touching && getParent() != null) getParent().requestDisallowInterceptTouchEvent(value);
        }

        @Override public boolean dispatchTouchEvent(MotionEvent event) {
            int action = event.getActionMasked();
            if (action == MotionEvent.ACTION_DOWN) {
                touching = true;
                if (contain && getParent() != null) getParent().requestDisallowInterceptTouchEvent(true);
            }
            try { return super.dispatchTouchEvent(event); }
            finally {
                if (action == MotionEvent.ACTION_UP || action == MotionEvent.ACTION_CANCEL) {
                    touching = false;
                    if (contain && getParent() != null) getParent().requestDisallowInterceptTouchEvent(false);
                }
            }
        }
    }

    // RecyclerView owns cell lifetimes; managed row subtrees remain keyed by node ID.
    private static final class NativeCollection extends RecyclerView {
        final NodeView owner;
        final LinearLayoutManager manager;
        final Rows adapter;
        View[] rows=new View[0];
        int[] offsets=new int[]{0};
        boolean applying=false,contain=true;
        int pendingOffset=-1;
        NativeCollection(NativeHost host,NodeView owner){
            super(host);this.owner=owner;
            manager=new LinearLayoutManager(host);
            manager.setItemPrefetchEnabled(false);
            setLayoutManager(manager);setItemAnimator(null);setItemViewCacheSize(0);
            getRecycledViewPool().setMaxRecycledViews(0,32);
            adapter=new Rows();adapter.setHasStableIds(true);setAdapter(adapter);
            setClipChildren(true);setClipToPadding(true);
            addOnScrollListener(new OnScrollListener(){@Override public void onScrolled(RecyclerView view,int dx,int dy){report();}});
        }
        void setContain(boolean value){
            contain=value;
            if(value){stopNestedScroll(androidx.core.view.ViewCompat.TYPE_TOUCH);stopNestedScroll(androidx.core.view.ViewCompat.TYPE_NON_TOUCH);}
            setNestedScrollingEnabled(!value);
        }
        int offset(){
            if(pendingOffset>=0)return pendingOffset;
            int first=manager.findFirstVisibleItemPosition();
            View view=first==NO_POSITION?null:manager.findViewByPosition(first);
            return view==null?0:Math.max(0,offsets[Math.min(first,rows.length)]-manager.getDecoratedTop(view));
        }
        void update(View[] next,float[] logical){
            float density=getResources().getDisplayMetrics().density;
            int[] nextOffsets=new int[logical.length];
            for(int i=0;i<logical.length;i++)nextOffsets[i]=Math.round(logical[i]*density);
            boolean changed=!java.util.Arrays.equals(rows,next)||!java.util.Arrays.equals(offsets,nextOffsets);
            if(!changed && !owner.pendingScroll)return;
            int target=owner.pendingScroll?owner.requestedScroll:offset();
            View[] oldRows=rows;int[] oldOffsets=offsets;
            applying=true;
            DiffUtil.DiffResult diff=DiffUtil.calculateDiff(new DiffUtil.Callback(){
                public int getOldListSize(){return oldRows.length;}
                public int getNewListSize(){return next.length;}
                public boolean areItemsTheSame(int a,int b){return ((NodeView)oldRows[a]).nodeId==((NodeView)next[b]).nodeId;}
                public boolean areContentsTheSame(int a,int b){return oldRows[a]==next[b] && oldOffsets[a+1]-oldOffsets[a]==nextOffsets[b+1]-nextOffsets[b];}
            },false);
            rows=next;offsets=nextOffsets;
            diff.dispatchUpdatesTo(adapter);
            target=Math.min(target,Math.max(0,offsets[offsets.length-1]-getHeight()));
            int position=0;while(position+1<rows.length && offsets[position+1]<=target)position++;
            if(rows.length>0)manager.scrollToPositionWithOffset(position,offsets[position]-target);
            pendingOffset=target;owner.pendingScroll=false;
            requestLayout();
        }
        @Override protected void onLayout(boolean changed,int l,int t,int r,int b){
            super.onLayout(changed,l,t,r,b);
            applying=false;pendingOffset=-1;report();
        }
        void report(){
            if(applying||!owner.observeScroll||owner.pendingScroll||getHeight()==0)return;
            float density=getResources().getDisplayMetrics().density;
            float position=offset()/density,viewport=getHeight()/density;
            if(position==owner.lastOffset && viewport==owner.lastViewport && owner.lastScrollRevision==owner.scrollRevision)return;
            owner.lastOffset=position;owner.lastViewport=viewport;owner.lastScrollRevision=owner.scrollRevision;
            event(owner.nodeId,10,position,owner.scrollRevision+":"+viewport);
        }
        @Override public boolean dispatchTouchEvent(MotionEvent event){
            if(event.getActionMasked()==MotionEvent.ACTION_DOWN && contain && getParent()!=null)getParent().requestDisallowInterceptTouchEvent(true);
            try{return super.dispatchTouchEvent(event);}finally{
                if((event.getActionMasked()==MotionEvent.ACTION_UP||event.getActionMasked()==MotionEvent.ACTION_CANCEL)&&getParent()!=null)getParent().requestDisallowInterceptTouchEvent(false);
            }
        }
        final class Rows extends RecyclerView.Adapter<Cell> {
            @Override public int getItemCount(){return rows.length;}
            @Override public long getItemId(int position){return Integer.toUnsignedLong(((NodeView)rows[position]).nodeId);}
            @Override public Cell onCreateViewHolder(ViewGroup parent,int type){return new Cell(new FrameLayout(getContext()));}
            @Override public void onBindViewHolder(Cell cell,int position){
                FrameLayout box=(FrameLayout)cell.itemView;
                box.removeAllViews();
                View row=rows[position];if(row.getParent()!=null)((ViewGroup)row.getParent()).removeView(row);
                box.setLayoutParams(new RecyclerView.LayoutParams(LayoutParams.MATCH_PARENT,Math.max(0,offsets[position+1]-offsets[position])));
                box.addView(row,new FrameLayout.LayoutParams(LayoutParams.MATCH_PARENT,LayoutParams.MATCH_PARENT));
            }
            @Override public void onViewRecycled(Cell cell){((FrameLayout)cell.itemView).removeAllViews();super.onViewRecycled(cell);}
        }
        static final class Cell extends RecyclerView.ViewHolder {Cell(FrameLayout box){super(box);}}
    }

    private static final class NodeView extends ViewGroup {
        android.graphics.Matrix projection;
        android.graphics.Bitmap backgroundBitmap;int backgroundFit=1;
        int gradientKind;float[] gradientOffsets;int[] gradientColors;float gradientSx,gradientSy,gradientEx,gradientEy;
        float[] cornerRadii=new float[4],borderWidths=new float[4];int[] borderColors=new int[4],borderStyles=new int[4];
        int boxColor,borderColor,shadowColor;float boxRadius,borderWidth,shadowBlur,shadowX,shadowY;boolean clip=true;
        final int kind;final int nodeId;float x,y,w,h,paddingLeft,paddingTop,paddingRight,paddingBottom,minimum,maximum=1;View content;View platformChild;String mountedType="";PlatformViewFactory mountedFactory;NodeView document;
        NodeView navigationPage, predictiveFrom, predictiveTo;
        View[] navigationPages;
        int navigationKind, navigationEasing, predictiveDirection;
        float navigationSeconds, predictiveProgress;
        int navigationDepth;
        boolean navigationBusy, canPop;
        android.animation.Animator navigationAnimation;
        @Override public boolean dispatchTouchEvent(MotionEvent event) {
            return navigationBusy || super.dispatchTouchEvent(event);
        }
        @Override protected void onDetachedFromWindow() {
            NativeHost host = (NativeHost)getContext();
            if (predictiveFrom != null && navigationAnimation == null) host.finishPredictive(this, false);
            if (navigationAnimation != null) navigationAnimation.end();
            if (host.backNavigation == this) { host.backNavigation = null; host.updateBack(0, false); }
            super.onDetachedFromWindow();
        }
        String fontAxes="";
        String[] tabLabels; int[] tabIcons;
        boolean updating;int focusRevision;
        private android.graphics.Rect scrollClip;
        int scrollRevision=0,requestedScroll=0;
        boolean observeScroll=false,pendingScroll=false,touchObserved=false;
        float lastOffset=-1,lastViewport=-1;
        int lastScrollRevision=-1;
        void reportScroll(){
            if(content instanceof NativeCollection){((NativeCollection)content).report();return;}
            if(!observeScroll || pendingScroll || getHeight()==0)return;
            boolean horizontal=content instanceof NativeHorizontalScroll;
            float viewport=(horizontal?content.getWidth():content.getHeight())/getResources().getDisplayMetrics().density;
            float offset=(horizontal?content.getScrollX():content.getScrollY())/getResources().getDisplayMetrics().density;
            offset=Math.max(0,offset);
            if(offset==lastOffset && viewport==lastViewport && lastScrollRevision==scrollRevision)return;
            lastOffset=offset;lastViewport=viewport;lastScrollRevision=scrollRevision;
            event(nodeId,10,offset,Integer.toUnsignedString(scrollRevision)+":"+Float.toString(viewport));
        }
        NodeView(NativeHost host,int id,int kind){this(host,id,kind,false);}
        NodeView(NativeHost host,int id,int kind,boolean virtualized){
            super(host);this.kind=kind;this.nodeId=id;setAddStatesFromChildren(kind==3);setClipChildren(kind==4);setClipToPadding(kind==4);
            switch(kind){
                case 14:{com.google.android.material.bottomnavigation.BottomNavigationView bar=newBottomNavigation(host);content=bar;
                    bar.setOnItemSelectedListener(item->{if(!updating && isEnabled())event(id,3,item.getItemId()-1, "");return true;});break;}
                case 13:content=new android.widget.Toolbar(host);break;
                case 2:content=new TextView(host);break;
                case 3:content=new Button(host){@Override public void setPressed(boolean pressed){boolean changed=isPressed()!=pressed;super.setPressed(pressed);if(changed)event(id,7,pressed?1:0,"");}};content.setOnClickListener(v->click(id));break;
                case 4:{if(virtualized){content=new NativeCollection(host,this);break;}NativeScroll scroll=new NativeScroll(host);scroll.setNestedScrollingEnabled(true);scroll.setClipChildren(true);scroll.setClipToPadding(true);document=new NodeView(host,0,1);scroll.addView(document);content=scroll;break;}
                case 5:{EditText field=new EditText(host);content=field;field.setSingleLine(true);
                    field.addTextChangedListener(new android.text.TextWatcher(){
                        public void beforeTextChanged(CharSequence s,int start,int count,int after){}
                        public void onTextChanged(CharSequence s,int start,int before,int count){if(!updating)event(id,2,0,s.toString());}
                        public void afterTextChanged(android.text.Editable text){}
                    });
                    field.setOnEditorActionListener((v,action,key)->{
                        boolean enter=key!=null && key.getKeyCode()==android.view.KeyEvent.KEYCODE_ENTER;
                        boolean multiline=(field.getInputType()&android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE)!=0;
                        if(action==android.view.inputmethod.EditorInfo.IME_NULL && (!enter || multiline))return false;
                        if(enter && key.getAction()!=android.view.KeyEvent.ACTION_DOWN)return true;
                        if(!updating)event(id,9,0,field.getText().toString());
                        return true;
                    });
                    field.setOnFocusChangeListener((v,focused)->{if(!updating)event(id,4,focused?1:0,"");});break;}
                case 12:{android.widget.RadioButton radio=new android.widget.RadioButton(host){@Override public void setPressed(boolean pressed){boolean changed=isPressed()!=pressed;super.setPressed(pressed);if(changed)event(id,7,pressed?1:0,"");}};content=radio;radio.setOnClickListener(v->{if(!updating)event(id,3,1,"");});break;}
                case 6:{android.widget.Switch toggle=new android.widget.Switch(host);content=toggle;toggle.setOnCheckedChangeListener((v,checked)->{if(!updating)event(id,3,checked?1:0,"");});break;}
                case 7:{SeekBar slider=new SeekBar(host);content=slider;slider.setMax(10000);slider.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){
                    public void onProgressChanged(SeekBar bar,int progress,boolean user){if(user&&!updating)event(id,3,minimum+(maximum-minimum)*progress/10000f,"");}
                    public void onStartTrackingTouch(SeekBar bar){} public void onStopTrackingTouch(SeekBar bar){}
                });break;}
                case 8:case 9:{ImageView image=new ImageView(host);image.setScaleType(ImageView.ScaleType.FIT_CENTER);content=image;break;}
                case 16:{FrameLayout slot=new FrameLayout(host);slot.setClipChildren(true);content=slot;break;}
            }
            if(content!=null){
                addView(content);
                if(kind==4 && !(content instanceof NativeCollection))host.observeScroll(this);
                if(kind==3 || kind==5 || kind==6 || kind==7 || kind==12){
                    content.setOnHoverListener((v,e)->{if(e.getAction()==android.view.MotionEvent.ACTION_HOVER_ENTER)event(id,8,1,"");else if(e.getAction()==android.view.MotionEvent.ACTION_HOVER_EXIT)event(id,8,0,"");return false;});
                    if(kind!=5)content.setOnFocusChangeListener((v,focused)->event(id,4,focused?1:0,""));
                }
            }
        }
        @Override public void draw(android.graphics.Canvas canvas) {
            int saved=canvas.save();
            if(projection!=null){canvas.translate(getWidth()/2f,getHeight()/2f);canvas.concat(projection);canvas.translate(-getWidth()/2f,-getHeight()/2f);}
            super.draw(canvas);canvas.restoreToCount(saved);
        }
        @Override protected void dispatchDraw(android.graphics.Canvas canvas){
            BoxShape shape=new BoxShape(this,new android.graphics.RectF(0,0,getWidth(),getHeight()));int save=canvas.save();if(clip)canvas.clipPath(shape.outer);
            super.dispatchDraw(canvas);canvas.restoreToCount(save);drawBorders(canvas,this,shape);
        }
        public void mountPlatformView(int nodeId, String type) {
            if (kind != 16 || !(content instanceof FrameLayout slot) || type == null) return;
            if (type.equals(mountedType) && platformChild != null) {
                if (mountedFactory != null) mountedFactory.rebind(platformChild, nodeId);
                return;
            }
            detachPlatformView();
            mountedType = type;
            mountedFactory = platformViewFactories.get(type);
            if (mountedFactory == null) return;
            View child = mountedFactory.create(nodeId);
            if (child == null) return;
            platformChild = child;
            slot.addView(child, new FrameLayout.LayoutParams(-1, -1));
        }
        public void detachPlatformView() {
            if (platformChild != null && mountedFactory != null) mountedFactory.detach(platformChild, nodeId);
            if (content instanceof ViewGroup slot) slot.removeAllViews();
            platformChild = null;
            mountedType = "";
            mountedFactory = null;
        }
        NodeView childrenHost(){return document==null?this:document;}
        @Override protected ViewGroup.LayoutParams generateDefaultLayoutParams(){return new ViewGroup.LayoutParams(-2,-2);}
        @Override protected void onMeasure(int width,int height){
            int measuredW=MeasureSpec.getSize(width),measuredH=MeasureSpec.getSize(height);
            if(MeasureSpec.getMode(width)==MeasureSpec.UNSPECIFIED){for(int i=0;i<getChildCount();i++){View child=getChildAt(i);if(child instanceof NodeView){NodeView n=(NodeView)child;measuredW=Math.max(measuredW,Math.round(n.x+n.w));}}}
            if(MeasureSpec.getMode(height)==MeasureSpec.UNSPECIFIED){for(int i=0;i<getChildCount();i++){View child=getChildAt(i);if(child instanceof NodeView){NodeView n=(NodeView)child;measuredH=Math.max(measuredH,Math.round(n.y+n.h));}}}
            setMeasuredDimension(measuredW,measuredH);
            for(int i=0;i<getChildCount();i++){
                View v=getChildAt(i);int cw,ch;
                if(v==content){cw=Math.max(0,Math.round(measuredW-(kind==4?0:paddingLeft+paddingRight)));ch=Math.max(0,Math.round(measuredH-(kind==4?0:paddingTop+paddingBottom)));}
                else{NodeView n=(NodeView)v;cw=Math.max(0,Math.round(n.w));ch=Math.max(0,Math.round(n.h));}
                v.measure(MeasureSpec.makeMeasureSpec(cw,MeasureSpec.EXACTLY),MeasureSpec.makeMeasureSpec(ch,MeasureSpec.EXACTLY));
            }
        }
        @Override protected void onLayout(boolean changed,int l,int t,int r,int b){
            for(int i=0;i<getChildCount();i++){
                View v=getChildAt(i);int x,y;
                if(v==content){x=kind==4?0:Math.round(paddingLeft);y=kind==4?0:Math.round(paddingTop);}else{NodeView n=(NodeView)v;x=Math.round(n.x);y=Math.round(n.y);}
                v.layout(x,y,x+v.getMeasuredWidth(),y+v.getMeasuredHeight());
                if(kind==4 && v==content && !(content instanceof NativeCollection)){
                    // Ancestor layout nodes allow overflow. The scroll viewport
                    // still needs its own explicit clip, including during flings.
                    if(scrollClip==null)scrollClip=new android.graphics.Rect();
                    scrollClip.set(0,0,v.getMeasuredWidth(),v.getMeasuredHeight());
                    v.setClipBounds(scrollClip);
                    if(pendingScroll){
                        pendingScroll=false;
                        if(content instanceof NativeHorizontalScroll)content.scrollTo(requestedScroll,0);
                        else content.scrollTo(0,requestedScroll);
                    }
                    reportScroll();
                }
            }
        }
    }
}
