using DotNative;
using DotNative.NativeDatePicker;
using DotNative.WheelDatePicker;

namespace DatePickerDemo;

public static class Program
{
    public static void Main()
    {
        var builder = DotNativeApplication.CreateBuilder();
        builder.Services.AddWheelDatePicker();
        builder.Services.AddNativeDatePicker();
        builder.Build().Run<App>();
    }
}
