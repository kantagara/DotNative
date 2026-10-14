package com.dotnative.runtime;

import android.test.InstrumentationTestCase;
import android.widget.EditText;
import android.view.ContextThemeWrapper;
import android.text.InputType;
import android.text.method.PasswordTransformationMethod;
import android.view.inputmethod.EditorInfo;

/** Tests actual Android editor configuration without starting an app session. */
@SuppressWarnings("deprecation")
public final class TextInputTest extends InstrumentationTestCase {
    private void configure(EditText field,int keyboard,int action,int capitalization,int autofill,int flags) {
        try {
            java.lang.reflect.Method method=NativeHost.class.getDeclaredMethod("configureInput",EditText.class,int.class,int.class,int.class,int.class,int.class);
            method.setAccessible(true);
            method.invoke(null,field,keyboard,action,capitalization,autofill,flags);
        } catch(Exception ex) { throw new AssertionError(ex); }
    }
    public void testPasswordResetAndMultiline() {
        getInstrumentation().runOnMainSync(() -> {
            EditText field=new EditText(new ContextThemeWrapper(getInstrumentation().getTargetContext(),android.R.style.Theme_Material_Light));
            field.setText("secret🙂");field.setSelection(2,4);
            configure(field,0,1,0,2,1);
            assertTrue(field.getTransformationMethod() instanceof PasswordTransformationMethod);
            assertEquals("secret🙂",field.getText().toString());
            assertEquals(2,field.getSelectionStart());assertEquals(4,field.getSelectionEnd());
            assertEquals(EditorInfo.IME_ACTION_DONE,field.getImeOptions()&EditorInfo.IME_MASK_ACTION);
            assertEquals("password",field.getAutofillHints()[0]);
            configure(field,1,2,0,4,4);
            assertFalse(field.getTransformationMethod() instanceof PasswordTransformationMethod);
            assertEquals(InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS,field.getInputType()&InputType.TYPE_MASK_VARIATION);
            assertEquals(EditorInfo.IME_ACTION_NEXT,field.getImeOptions()&EditorInfo.IME_MASK_ACTION);
            configure(field,0,0,0,0,4);
            assertNull(field.getAutofillHints());
            assertEquals(0,field.getInputType()&InputType.TYPE_MASK_VARIATION);
            configure(field,0,0,1,0,6);
            assertTrue((field.getInputType()&InputType.TYPE_TEXT_FLAG_MULTI_LINE)!=0);
            assertTrue((field.getImeOptions()&EditorInfo.IME_FLAG_NO_ENTER_ACTION)!=0);
            assertNull(field.getTransformationMethod());
            field.setText("first\nsecond");
            assertEquals("first\nsecond",field.getText().toString());
            configure(field,0,4,1,0,6);
            assertEquals(EditorInfo.IME_ACTION_SEND,field.getImeOptions()&EditorInfo.IME_MASK_ACTION);
            assertEquals(0,field.getImeOptions()&EditorInfo.IME_FLAG_NO_ENTER_ACTION);
        });
    }
}
