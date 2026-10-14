# Date picker demo

Both plugins are optional and independently registered. Each button calls only
its corresponding existing plugin service. No additional DI abstraction is used.
WheelDatePicker uses native mobile wheels on `dotnative.wheeldatepicker` and a
shared wheel overlay on desktop. NativeDatePicker uses the distinct
`dotnative.nativedatepicker` channel for calendar selection. The shared wheel can
also be chosen explicitly.

Choose **Wheel locale** before opening either wheel button. The same date appears
as day/month/year for `sr-Latn-RS` and `en-GB`, month/day/year for `en-US`, and
year/month/day for `ja-JP`. The result uses that locale's short date format.
This selection applies to WheelDatePicker; the separate native calendar keeps
its operating system locale.

Place both plugin repositories beside the framework checkout.
Run `dotnative run mac --hot-reload --project Samples/DatePickerDemo`.

Set `DOTNATIVE_DEMO_OPEN_PICKER=wheel` to open the wheel plugin immediately on startup.
