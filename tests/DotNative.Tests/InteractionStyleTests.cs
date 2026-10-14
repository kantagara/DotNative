using System.Buffers.Binary;
using DotNative;

internal static class InteractionStyleTests
{
    public static void Run()
    {
        StateLifecycle(new RadioButton("focus", true, () => { }), true);
        StateLifecycle(new Switch(true, _ => { }), true);
        StateLifecycle(new Slider(.5f, _ => { }), false);
        StateLifecycle(new TextField("", _ => { }), false, InteractionState.Hovered, 8);
        StateLifecycle(
            new RadioButton("hover", true, () => { }),
            true,
            InteractionState.Hovered,
            8
        );
        StateLifecycle(new Switch(true, _ => { }), true, InteractionState.Hovered, 8);
        StateLifecycle(new Slider(.5f, _ => { }), false, InteractionState.Hovered, 8);
        StateLifecycle(
            new RadioButton("pressed", true, () => { }),
            true,
            InteractionState.Pressed,
            7
        );
        RejectPressed(new TextField("", _ => { }));
        RejectPressed(new Switch(false, _ => { }));
        RejectPressed(new Slider(.5f, _ => { }));
        int clicks = 0;
        var plain = new Button("states", () => clicks++).BackgroundColor(Color.Blue);
        var styled = plain
            .OnState(InteractionState.Hovered, new(Opacity: .8f))
            .OnState(InteractionState.Pressed, new(Opacity: .5f, Background: Color.Red))
            .OnState(InteractionState.Disabled, new(Opacity: .25f));
        var state = new State<Button>(styled);
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        using var host = new RenderHost(
            new Factory(() => new VStack(new Container(state.Value))),
            dispatcher,
            native
        );
        host.RenderNow();
        var id = native.Find("states");
        Send(8, 1);
        Check(Opacity() == .8f);
        Send(7, 1);
        Check(Opacity() == .5f);
        Send(7, 0);
        Check(Opacity() == .8f);
        Send(8, 0);
        Check(Opacity() == 1);
        Check(host.DispatchClick(id) && clicks == 1);
        Send(7, 1);
        state.Value = styled.Enabled(false);
        dispatcher.Drain();
        Check(
            !native.Nodes[id].Enabled && Opacity() == .25f && !host.DispatchClick(id) && clicks == 1
        );
        state.Value = styled;
        dispatcher.Drain();
        Check(native.Nodes[id].Enabled && Opacity() == 1);
        Send(8, 1);
        state.Value = plain;
        dispatcher.Drain();
        Check(Opacity() == 1 && native.Find("states") == id);
        try
        {
            plain.OnState(InteractionState.Selected, new());
            throw new Exception("Unsupported state accepted");
        }
        catch (ArgumentException) { }
        var toggleState = new State<bool>(false);
        var controlsEnabled = new State<bool>(true);
        var controlsNative = new NativeModel();
        int toggleCalls = 0,
            sliderCalls = 0;
        using var controls = new RenderHost(new Factory(() => new VStack(new Switch(
                        toggleState.Value,
                        value =>
                        {
                            toggleCalls++;
                            toggleState.Value = value;
                        }
                    ).OnState(InteractionState.Selected, new(Opacity: .7f)).OnState(InteractionState.Disabled, new(Opacity: .2f)).Enabled(controlsEnabled.Value), new Slider(.5f, _ => sliderCalls++).OnState(InteractionState.Disabled, new(Opacity: .3f)).Enabled(controlsEnabled.Value))), dispatcher, controlsNative);
        controls.RenderNow();
        var controlIds = controlsNative.Nodes[controlsNative.Root].Children.ToArray();
        EmitValue(controlIds[0], 1);
        Check(toggleState.Value && toggleCalls == 1 && ControlOpacity(0) == .7f);
        controlsEnabled.Value = false;
        dispatcher.Drain();
        Check(ControlOpacity(0) == .2f && ControlOpacity(1) == .3f);
        EmitValue(controlIds[0], 0);
        EmitValue(controlIds[1], .75f);
        Check(toggleCalls == 1 && sliderCalls == 0);
        controlsEnabled.Value = true;
        toggleState.Value = false;
        dispatcher.Drain();
        Check(ControlOpacity(0) == 1 && ControlOpacity(1) == 1);
        Check(controlsNative.Nodes[controlsNative.Root].Children.SequenceEqual(controlIds));
        try
        {
            new Slider(.5f, _ => { }).OnState(InteractionState.Selected, new());
            throw new Exception("Slider selection state accepted");
        }
        catch (ArgumentException) { }
        try
        {
            new Switch(false, _ => { }).OnState(
                InteractionState.Selected,
                new(Foreground: Color.Red)
            );
            throw new Exception("Nontextual foreground state accepted");
        }
        catch (ArgumentException) { }
        float ControlOpacity(int index) =>
            BinaryPrimitives.ReadSingleLittleEndian(
                controlsNative.Nodes[controlIds[index]].VisualStyle
            );
        void EmitValue(uint controlId, float value)
        {
            var data = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(data, controlId);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 3);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(8), value);
            NativeTransport.DispatchEvent(data, controls);
            dispatcher.Drain();
        }
        void Send(uint kind, float value)
        {
            var data = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(data, id);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), kind);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(8), value);
            NativeTransport.DispatchEvent(data, host);
            dispatcher.Drain();
        }
        float Opacity() => BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[id].VisualStyle);
    }

    private static void StateLifecycle<T>(
        T control,
        bool selected,
        InteractionState interaction = InteractionState.Focused,
        uint eventKind = 4
    )
        where T : Node, IStateStyled
    {
        var styled = control.OnState(interaction, new(Opacity: .6f));
        if (selected)
            styled = styled.OnState(interaction | InteractionState.Selected, new(Opacity: .4f));
        styled = styled.OnState(InteractionState.Disabled, new(Opacity: .2f));
        var state = new State<T>(styled);
        var dispatcher = new Dispatcher();
        var native = new NativeModel();
        using var host = new RenderHost(
            new Factory(() => new VStack(state.Value)),
            dispatcher,
            native
        );
        host.RenderNow();
        var id = native.Nodes[native.Root].Children.Single();
        Send(1);
        Check(Opacity() == (selected ? .4f : .6f));
        Send(0);
        Check(Opacity() == 1);
        Send(1);
        state.Value = styled.Enabled(false);
        dispatcher.Drain();
        Check(Opacity() == .2f);
        Send(1); // A late native interaction notification cannot reactivate a disabled style.
        Check(Opacity() == .2f);
        state.Value = styled;
        dispatcher.Drain();
        Check(Opacity() == 1);
        Send(1);
        state.Value = styled.ClearStateStyles();
        dispatcher.Drain();
        Check(Opacity() == 1 && native.Nodes[native.Root].Children.Single() == id);
        foreach (float invalid in new[] { -1f, .5f, 2f })
        {
            try
            {
                Send(invalid);
                throw new Exception("Nonboolean interaction accepted");
            }
            catch (InvalidOperationException) { }
        }
        float Opacity() => BinaryPrimitives.ReadSingleLittleEndian(native.Nodes[id].VisualStyle);
        void Send(float value)
        {
            var data = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(data, id);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), eventKind);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(8), value);
            NativeTransport.DispatchEvent(data, host);
            dispatcher.Drain();
        }
    }

    private static void RejectPressed<T>(T control)
        where T : Node, IStateStyled
    {
        try
        {
            control.OnState(InteractionState.Pressed, new());
            throw new Exception("Unsupported press accepted");
        }
        catch (ArgumentException) { }
    }

    private static void Check(bool value)
    {
        if (!value)
            throw new Exception("Interaction style contract failed");
    }
}
