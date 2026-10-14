# Shared wheel picker and overlays

`WheelPicker` renders bounded or looping items through DotNative's common component tree.
Pass the items, initial index, change callback and UI dispatcher to its constructor.
`SetItems` updates dependent ranges on the UI thread. It uses native scrolling,
centered row layout, projective transforms and animated snapping.

`IOverlayPresenter.ShowAsync<T>` creates a component above the application root.
The factory receives a close callback; closing returns a value and disposes the
component if it implements IDisposable. Cancellation removes the overlay.
Calls are serialized. `ViewportSize` in BuildContext provides the current host size.

ProjectiveTransform applies a finite Matrix4x4 around the visual box center.
Coordinates are logical pixels. It does not change layout or transform input hit
regions. Put projected passive content above an unprojected interaction surface,
as WheelPicker does. Desktop renderers use their own native transform APIs.

WheelDatePicker selects native wheel controls on iOS and Android. Its desktop
fallback uses three shared wheels, dependent calendar ranges, font registration
and an animated overlay. The fallback can be selected explicitly on mobile too.
NativeDatePicker wraps operating-system calendar controls instead.

When scrolling stops, the shared wheel commits an offset that is an exact multiple
of ItemExtent. The final native scroll request is written explicitly, so dropping
the last animation frame cannot leave a value between rows.

Visual parity and motion against the original picker are not yet certified.

The wheel predicts a natural friction stop, selects the nearest fixed-extent row,
and adjusts friction to arrive there with a low final velocity. Short movements
settle with an overdamped spring instead of a fixed-duration easing curve. Mouse
release velocity on macOS is estimated from recent movement when the gesture
recognizer returns zero. Starting a new drag cancels the running simulation.
The adapted physics attribution and BSD license are included in THIRD_PARTY_NOTICES.
