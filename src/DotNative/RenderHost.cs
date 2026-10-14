namespace DotNative;

// One instance owns one native engine session. All public operations except
// RequestRebuild run on the OS main thread supplied by the runner dispatcher.
public sealed partial class RenderHost : IDisposable
{
    private sealed record Snapshot(
        uint Id,
        Node Node,
        Snapshot[] Children,
        ControlProperties Control
    );

    private readonly Component _root;
    private readonly IUiDispatcher _dispatcher;
    private readonly Hosting.DispatcherSynchronizationContext _synchronizationContext;
    private readonly ICommandTransport _transport;
    private Exception? _transportFailure;
    private readonly CommandBuffer _buffer = new();
    private readonly Dictionary<(string Family, string Hash), uint> _registeredFonts = new();
    private Dictionary<(string Family, string Hash), uint> _pendingFonts = new();
    private readonly ElementResolver _resolver;
    private ElementResolver.Entry? _resolved;
    private int _forceRebuild;
    private Dictionary<uint, Action> _clicks = [];
    private Dictionary<uint, Node> _inputs = [];
    private Snapshot? _current;
    private uint _lastId;
    private readonly Dictionary<uint, InteractionState> _interactionStates = [];
    private bool _interactionDirty;
    private int _queued;
    private volatile bool _disposed;
    private bool _rendering,
        _faulted;
    private float _width,
        _height,
        _safeTop,
        _safeBottom,
        _keyboard;
    private bool _viewportDirty = true;
    internal int LastResolvedElements => _resolver.Visited;
    internal int LastDiffedNodes { get; private set; }

    public RenderHost(
        Component root,
        IUiDispatcher dispatcher,
        ICommandTransport transport,
        TimeProvider? clock = null
    )
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _synchronizationContext = new(dispatcher);
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _resolver = new ElementResolver(ScheduleRender);
        _clock = clock ?? TimeProvider.System;
        VerifyThread();
        HotReload.Register(this);
    }

    public void SetViewport(float width, float height)
    {
        VerifyUsable();
        Styling.Nonnegative(width);
        Styling.Nonnegative(height);
        if (Math.Abs(_width - width) < float.Epsilon && Math.Abs(_height - height) < float.Epsilon)
            return;
        _width = width;
        _height = height;
        _viewportDirty = true;
        RequestRebuild();
    }

    public void SetWindowInsets(float top, float bottom, float keyboard)
    {
        VerifyUsable();
        Styling.Nonnegative(top);
        Styling.Nonnegative(bottom);
        Styling.Nonnegative(keyboard);
        if (
            Math.Abs(_safeTop - top) < float.Epsilon
            && Math.Abs(_safeBottom - bottom) < float.Epsilon
            && Math.Abs(_keyboard - keyboard) < float.Epsilon
        )
            return;
        _safeTop = top;
        _safeBottom = bottom;
        _keyboard = keyboard;
        RequestRebuild();
    }

    public void RequestRebuild()
    {
        Interlocked.Exchange(ref _forceRebuild, 1);
        ScheduleRender();
    }

    private void ScheduleRender()
    {
        if (_disposed || Interlocked.Exchange(ref _queued, 1) != 0)
            return;
        try
        {
            _dispatcher.Post(() =>
            {
                if (Interlocked.Exchange(ref _queued, 0) == 0 || _disposed || _faulted)
                    return;
                Render(force: false);
            });
        }
        catch
        {
            Interlocked.Exchange(ref _queued, 0);
            throw;
        }
    }

    public void RenderNow() => Render(force: true);

    private void Render(bool force)
    {
        VerifyUsable();
        if (BuildScope.Reads is not null)
            throw new InvalidOperationException("Nested rendering is forbidden.");
        Interlocked.Exchange(ref _queued, 0);
        _rendering = true;
        force |= Interlocked.Exchange(ref _forceRebuild, 0) != 0;
        try
        {
            // Resolve dirty branches before writing commands; reuse clean descriptions.
            // Failed Build leaves the committed tree/subscriptions intact.
            BuildScope.Reads = [];
            ElementResolver.Entry next;
            var lifetime = new BuildContext.Lifetime();
            try
            {
                next = _resolver.Resolve(
                    _root,
                    _resolved,
                    new BuildContext(lifetime)
                        .With(new ViewportSize(_width, _height))
                        .With(new WindowInsets(_safeTop, _safeBottom, _keyboard)),
                    force
                );
            }
            finally
            {
                lifetime.Active = false;
                BuildScope.Reads = null;
                BuildScope.ContextReads = null;
            }
            _buffer.Reset();
            if (_viewportDirty)
                _buffer.SetViewport(_width, _height);
            _pendingMotions = new(_motions);
            _pendingFonts = new(_registeredFonts);
            var obsolete = new List<Snapshot>();
            var updates = new List<Snapshot>();
            LastDiffedNodes = 0;
            Snapshot candidate = Diff(next.Node, _current, obsolete, updates);
            var scrollControllers = new HashSet<ScrollController>(
                ReferenceEqualityComparer.Instance
            );
            foreach (var snapshot in Enumerate(candidate))
                if (
                    snapshot.Node is ScrollView { Controller: { } controller }
                    && !scrollControllers.Add(controller)
                )
                    throw new InvalidOperationException(
                        "A ScrollController can belong to only one mounted ScrollView."
                    );
            if (_current?.Id != candidate.Id)
                _buffer.SetRoot(candidate.Id);
            foreach (var removed in obsolete)
                DeleteTree(removed);
            Flush();
            _registeredFonts.Clear();
            foreach (var font in _pendingFonts)
                _registeredFonts.Add(font.Key, font.Value);
            _current = candidate;
            _motions = _pendingMotions;
            foreach (var removed in obsolete)
            foreach (var snapshot in Enumerate(removed))
            {
                _interactionStates.Remove(snapshot.Id);
                _motions.Remove(snapshot.Id);
                _clicks.Remove(snapshot.Id);
                _inputs.Remove(snapshot.Id);
            }
            foreach (var snapshot in updates)
            {
                if (!snapshot.Node.IsEnabled)
                    _interactionStates.Remove(snapshot.Id);
                if (snapshot.Node is Button button)
                    _clicks[snapshot.Id] = button.OnClick;
                if (
                    snapshot.Node
                    is IInteractive
                        or TextField
                        or ScrollView
                        or MeasuredItem
                        or Switch
                        or Slider
                        or RadioButton
                        or NativeNavigationNode
                        or NativeNavigationPage
                        or NativeHeaderNode
                )
                    _inputs[snapshot.Id] = snapshot.Node;
            }
            _resolver.Commit();
            _resolved = next;
            _viewportDirty = false;
            _accessibilityDirty = false;
            _interactionDirty = false;
            UpdateMotionTimer();
        }
        catch
        {
            // A failed full rebuild must remain full on the next attempt.
            if (force)
                Interlocked.Exchange(ref _forceRebuild, 1);
            throw;
        }
        finally
        {
            _resolver.Clear();
            _rendering = false;
        }
    }

    public bool DispatchClick(uint nodeId)
    {
        VerifyUsable();
        if (_inputs.TryGetValue(nodeId, out var control) && !control.IsEnabled)
            return false;
        if (!_clicks.TryGetValue(nodeId, out var action))
            return false;
        _synchronizationContext.Run(action);
        return true;
    }

    private static IEnumerable<Snapshot> Enumerate(Snapshot snapshot)
    {
        yield return snapshot;
        foreach (var child in snapshot.Children)
        foreach (var item in Enumerate(child))
            yield return item;
    }

    internal Action<ApplicationActivation>? ActivationReceived { get; set; }

    internal void DispatchInput(uint id, NativeEventKind kind, float number, string text)
    {
        VerifyUsable();
        _synchronizationContext.Run(() => DispatchInputCore(id, kind, number, text));
    }

    private void DispatchInputCore(uint id, NativeEventKind kind, float number, string text)
    {
        if (id == 0 && kind is NativeEventKind.OpenUri or NativeEventKind.OpenNotification)
        {
            ActivationReceived?.Invoke(
                new(
                    kind == NativeEventKind.OpenUri
                        ? ApplicationActivationKind.Uri
                        : ApplicationActivationKind.Notification,
                    text
                )
            );
            return;
        }
        if (
            kind
            is NativeEventKind.FocusChanged
                or NativeEventKind.PressedChanged
                or NativeEventKind.HoverChanged
        )
        {
            if (!_inputs.TryGetValue(id, out var interactive))
                return;
            var flag = kind switch
            {
                NativeEventKind.FocusChanged => InteractionState.Focused,
                NativeEventKind.PressedChanged => InteractionState.Pressed,
                _ => InteractionState.Hovered,
            };
            var before = _interactionStates.GetValueOrDefault(id);
            var after = number != 0 && interactive.IsEnabled ? before | flag : before & ~flag;
            if (before != after)
            {
                _interactionStates[id] = after;
                _interactionDirty = true;
                ScheduleRender();
            }
            if (kind != NativeEventKind.FocusChanged)
                return;
        }
        if (kind == NativeEventKind.Click)
        {
            DispatchClick(id);
            return;
        }
        if (
            !_inputs.TryGetValue(id, out var node)
            || (
                !node.IsEnabled
                && kind
                    is not (
                        NativeEventKind.FocusChanged
                        or NativeEventKind.LayoutMeasured
                        or NativeEventKind.ScrollChanged
                    )
            )
        )
            return;
        if (
            kind == NativeEventKind.ValueChanged
            && (node is NativeNavigationPage || node is NativeHeaderNode)
        )
        {
            var options = node is NativeNavigationPage page
                ? page.Options
                : ((NativeHeaderNode)node).Options;
            if (node is NativeHeaderNode header && number == -1)
            {
                header.Back();
                return;
            }
            var actions = options.Leading.Concat(options.Trailing).ToArray();
            if (
                options.HeaderVisible
                && float.IsFinite(number)
                && number >= 0
                && number < actions.Length
                && number == MathF.Truncate(number)
            )
                actions[(int)number].Execute();
            return;
        }
        switch (node, kind)
        {
            case (NativeNavigationNode navigation, NativeEventKind.NavigationShown):
                navigation.Shown(checked((int)number));
                break;
            case (NativeNavigationNode navigation, NativeEventKind.NavigationStarted):
                navigation.Transition(true);
                break;
            case (MeasuredItem item, NativeEventKind.LayoutMeasured):
                if (
                    ScrollController.Decode(number, text, out var measureRevision, out var measured)
                    && measureRevision == item.Revision
                    && measured.ViewportExtent > 0
                )
                    item.Measured(measured.Offset, measured.ViewportExtent);
                break;
            case (ScrollView fling, NativeEventKind.ScrollFling) when float.IsFinite(number):
                fling.OnFling?.Invoke(number);
                break;
            case (ScrollView dragging, NativeEventKind.ScrollDraggingChanged):
                dragging.OnDraggingChanged?.Invoke(number != 0);
                break;
            case (ScrollView tapped, NativeEventKind.ScrollTapped) when float.IsFinite(number):
                tapped.OnTapped?.Invoke(number);
                break;
            case (ScrollView scroll, NativeEventKind.ScrollChanged):
                if (
                    ScrollController.Decode(number, text, out var revision, out var metrics)
                    && revision == scroll.ResolvedControl.FocusRevision
                )
                {
                    // A callback can reset the controller before the new tree is
                    // committed. Reject queued reports from its previous position.
                    if (scroll.Controller is null || scroll.Controller.Report(revision, metrics))
                        scroll.OnScrolled?.Invoke(metrics);
                }
                break;
            case (TextField field, NativeEventKind.TextSubmitted):
                field.OnSubmitted?.Invoke(text);
                break;
            case (TextField field, NativeEventKind.TextChanged):
                field.OnChanged(text);
                break;
            case (TextField field, NativeEventKind.FocusChanged):
                field.OnFocusChanged?.Invoke(number != 0);
                break;
            case (NativeDesktopNavigation sidebar, NativeEventKind.ValueChanged):
                if (number == -1)
                    sidebar.OnOpenChanged(!sidebar.IsOpen);
                else if (
                    float.IsFinite(number)
                    && number == MathF.Truncate(number)
                    && number >= 0
                    && number < sidebar.Items.Length
                )
                    sidebar.Items[(int)number].OnClick();
                break;
            case (BottomNavigationBar bar, NativeEventKind.ValueChanged)
                when float.IsFinite(number)
                    && number == MathF.Truncate(number)
                    && number >= 0
                    && number < bar.Items.Count:
                bar.OnTap((int)number);
                break;
            case (RadioButton radio, NativeEventKind.ValueChanged)
                when number == 1 && !radio.IsSelected:
                radio.OnSelected();
                break;
            case (Switch toggle, NativeEventKind.ValueChanged):
                toggle.OnChanged(number != 0);
                break;
            case (Slider slider, NativeEventKind.ValueChanged):
                slider.OnChanged(Math.Clamp(number, slider.Minimum, slider.Maximum));
                break;
        }
    }

    internal static ControlProperties Control(Node node)
    {
        if (node is ScrollView scroll)
        {
            var request = scroll.Controller?.Request ?? default;
            return new(
                Value: request.Offset,
                FocusRevision: request.Revision,
                FocusRequested: scroll.Controller is not null || scroll.OnScrolled is not null
            );
        }
        if (node is NativeNavigationNode navigation)
            return new(
                Value: navigation.Depth,
                Maximum: Math.Max(1, navigation.Children.Count),
                FocusRevision: (uint)navigation.Mode
            );
        if (node is TextField field)
        {
            ArgumentNullException.ThrowIfNull(field.Input);
            field.Input.Validate();
            var focus = field.Focus?.Read() ?? default;
            return new(
                FocusRevision: focus.Revision,
                FocusRequested: focus.Requested,
                Placeholder: field.Placeholder
            );
        }
        if (node is RadioButton radio)
            return new(Value: radio.IsSelected ? 1 : 0);
        if (node is Switch toggle)
            return new(Value: toggle.Value ? 1 : 0);
        if (node is Slider slider)
            return new(Value: slider.Value, Minimum: slider.Minimum, Maximum: slider.Maximum);
        return new();
    }

    private static ScreenOptions? Header(Node node) =>
        node switch
        {
            NativeNavigationPage page => page.Options,
            NativeHeaderNode header => header.Options,
            _ => null,
        };

    private static bool SameHeader(ScreenOptions a, ScreenOptions? b) =>
        b is not null
        && a.HeaderVisible == b.HeaderVisible
        && a.BackButtonVisible == b.BackButtonVisible
        && a.CanGoBack == b.CanGoBack
        && a.TitleDisplay == b.TitleDisplay
        && a.Leading.Select(x => x.Title).SequenceEqual(b.Leading.Select(x => x.Title))
        && a.Trailing.Select(x => x.Title).SequenceEqual(b.Trailing.Select(x => x.Title));

    private Snapshot Diff(Node node, Snapshot? old, List<Snapshot> obsolete, List<Snapshot> updates)
    {
        LastDiffedNodes++;
        if (
            !_accessibilityDirty
            && !_interactionDirty
            && old is not null
            && ReferenceEquals(node, old.Node)
        )
            return old;
        if (
            old is not null
            && (
                (
                    old.Node is ScrollView oldScroll
                    && node is ScrollView newScroll
                    && oldScroll.Virtualized != newScroll.Virtualized
                )
                || Kind(old.Node) != Kind(node)
                || old.Node.Key != node.Key
                || (
                    old.Node is TextField previousField
                    && node is TextField field
                    && previousField.Input.IsMultiline != field.Input.IsMultiline
                )
            )
        )
        {
            obsolete.Add(old);
            old = null;
        }
        uint id = old?.Id ?? NextId();
        if (node.InteractionRules.Length != 0)
        {
            var state = node.IsEnabled
                ? _interactionStates.GetValueOrDefault(id)
                : InteractionState.Disabled;
            if (node is RadioButton { IsSelected: true } or Switch { Value: true })
                state |= InteractionState.Selected;
            foreach (var rule in node.InteractionRules)
                if ((state & rule.State) == rule.State)
                    node = rule.Style.Apply(node);
        }
        node = ApplyAccessibility(node);
        ApplyNodeUpdates(id, node, old?.Node, old?.Control);

        var children = DiffChildren(id, node, old, obsolete, updates);
        var next = new Snapshot(id, node, children, node.ResolvedControl);
        updates.Add(next);
        return next;
    }

    private void ApplyNodeUpdates(uint id, Node node, Node? old, ControlProperties? oldControl)
    {
        bool isNew = old is null;
        (node, old) = TransitionNode(id, node, old);

        if (isNew)
            _buffer.Create(id, Kind(node));
        if (isNew ? !node.IsEnabled : node.IsEnabled != old!.IsEnabled)
            _buffer.SetEnabled(id, node.IsEnabled);
        if (
            node is Image fitted
            && (
                old is not Image priorImage
                    ? fitted.ImageFit != ImageFit.Contain
                    : fitted.ImageFit != priorImage.ImageFit
            )
        )
            _buffer.SetImageFit(id, fitted.ImageFit);
        if (
            node is Image tinted
            && (
                old is not Image previousTint
                    ? tinted.ImageTint is not null
                    : tinted.ImageTint != previousTint.ImageTint
            )
        )
            _buffer.SetImageTint(id, tinted.ImageTint);
        if (
            isNew
                ? node.BackgroundImageSource is not null
                : !ReferenceEquals(old!.BackgroundImageSource, node.BackgroundImageSource)
                    || old.BackgroundImageFit != node.BackgroundImageFit
        )
            _buffer.SetBackgroundImage(id, node.BackgroundImageSource, node.BackgroundImageFit);
        if (isNew ? node.Decoration != default : old!.Decoration != node.Decoration)
            _buffer.SetBoxDecoration(id, node.Decoration);
        if (
            node is Text rich
            && (
                old is not Text previousText
                    ? rich.Runs.Length != 0
                    : !rich.Runs.SequenceEqual(previousText.Runs)
            )
        )
            _buffer.SetTextRuns(id, rich.Runs);
        if (isNew ? node.ZIndex != 0 : old!.ZIndex != node.ZIndex)
            _buffer.SetZIndex(id, node.ZIndex);
        if (isNew ? node.Gradient is not null : !Equals(old!.Gradient, node.Gradient))
            _buffer.SetGradient(id, node.Gradient);
        if (
            isNew
                ? node.AdvancedLayout != AdvancedLayoutStyle.Default
                : old!.AdvancedLayout != node.AdvancedLayout
        )
            _buffer.SetAdvancedLayout(id, node.AdvancedLayout);
        if (isNew ? node.FontAsset is not null : !ReferenceEquals(old!.FontAsset, node.FontAsset))
        {
            if (node.FontAsset is { } font)
            {
                if (!_pendingFonts.TryGetValue(font.CacheKey, out var key))
                {
                    key = checked((uint)_pendingFonts.Count + 1);
                    _buffer.RegisterFont(key, font);
                    _pendingFonts.Add(font.CacheKey, key);
                }
                _buffer.SetFontReference(id, key);
            }
            else
                _buffer.SetFontSource(id, null);
        }
        if (
            isNew ? node.FontVariation is not null : !Equals(old!.FontVariation, node.FontVariation)
        )
            _buffer.SetFontVariations(id, node.FontVariation);
        if (isNew ? node.Typography != TypographyStyle.Default : old!.Typography != node.Typography)
            _buffer.SetTypography(id, node.Typography);
        if (
            isNew
                ? node.Projection != System.Numerics.Matrix4x4.Identity
                : old!.Projection != node.Projection
        )
            _buffer.SetProjection(id, node.Projection);
        if (isNew ? node.Visual != VisualStyle.Default : old!.Visual != node.Visual)
            _buffer.SetVisualStyle(id, node.Visual);
        if (isNew || old!.Style != node.Style)
            _buffer.SetStyle(id, node.Style);
        if (isNew ? node.Layout != default : old!.Layout != node.Layout)
            _buffer.SetLayout(id, node.Layout);
        if (Label(node) is { } text && (isNew || Label(old!) != text))
            _buffer.SetText(id, text);
        if (
            node is ScrollView scroll
            && (old is not ScrollView prior || scroll.NestedScrolling != prior.NestedScrolling)
        )
            _buffer.SetScroll(id, scroll.NestedScrolling);
        if (
            node is ScrollView axisScroll
            && (old is not ScrollView oldScroll || axisScroll.ScrollAxis != oldScroll.ScrollAxis)
        )
            _buffer.SetScrollAxis(id, axisScroll.ScrollAxis);

        if (isNew && node is ScrollView { Virtualized: true })
            _buffer.SetVirtualList(id);
        var observation = (node as MeasuredItem)?.Revision ?? 0;
        if (observation != ((old as MeasuredItem)?.Revision ?? 0))
            _buffer.ObserveLayout(id, observation);
        if (node is ScrollView && (isNew || oldControl != node.ResolvedControl))
            _buffer.SetScrollPosition(
                id,
                (
                    node.ResolvedControl.FocusRevision,
                    node.ResolvedControl.Value,
                    node.ResolvedControl.FocusRequested
                )
            );
        if (
            node is BottomNavigationBar bar
            && (
                old is not BottomNavigationBar oldBar
                || oldBar.CurrentIndex != bar.CurrentIndex
                || !oldBar.Items.SequenceEqual(bar.Items)
            )
        )
            _buffer.SetBottomNavigation(id, bar);
        if (
            node is NativeDesktopNavigation desktop
            && (
                old is not NativeDesktopNavigation previousDesktop
                || previousDesktop.Title != desktop.Title
                || previousDesktop.CurrentIndex != desktop.CurrentIndex
                || previousDesktop.IsOpen != desktop.IsOpen
                || previousDesktop.PaneWidth != desktop.PaneWidth
                || previousDesktop.ThemeMode != desktop.ThemeMode
                || !previousDesktop.Items.SequenceEqual(desktop.Items)
            )
        )
            _buffer.SetDesktopNavigation(id, desktop);
        if (Header(node) is { } header)
        {
            var oldHeader = old is not null ? Header(old) : null;
            if (
                oldHeader is null
                || oldHeader.HeaderBackgroundOpacity != header.HeaderBackgroundOpacity
            )
                _buffer.SetHeaderOpacity(id, header.HeaderBackgroundOpacity);
            if (!SameHeader(header, oldHeader))
                _buffer.SetHeader(id, header);
        }

        if (
            node is TextField or Switch or Slider or RadioButton or NativeNavigationNode
            && (isNew || oldControl != node.ResolvedControl)
        )
            _buffer.SetControl(id, node.ResolvedControl);
        if (
            node is TextField input
            && (old is not TextField previousInput || input.Input != previousInput.Input)
        )
            _buffer.SetTextInput(id, input.Input);
        if (
            node is NativeNavigationNode navigation
            && (
                old is not NativeNavigationNode previousNavigation
                || navigation.TransitionOptions != previousNavigation.TransitionOptions
            )
        )
            _buffer.SetNavigationTransition(id, navigation.TransitionOptions);
        if (
            node is Image image
            && (old is not Image previous || !ReferenceEquals(image.Source, previous.Source))
        )
            _buffer.SetImage(id, image.Source);
        if (
            node is PlatformView slot
            && (old is not PlatformView previousSlot || previousSlot.ViewType != slot.ViewType)
        )
            _buffer.SetPlatformView(id, slot.ViewType);
    }

    private Snapshot[] DiffChildren(
        uint parentId,
        Node node,
        Snapshot? old,
        List<Snapshot> obsolete,
        List<Snapshot> updates
    )
    {
        var oldChildren = old?.Children ?? [];
        var children = (node as ContainerNode)?.Children;
        if (children is null || children.Count == 0)
        {
            obsolete.AddRange(oldChildren);
            if (node is ContainerNode && (old is null || oldChildren.Length > 0))
                _buffer.SetChildren(parentId, []);
            return [];
        }

        // Resolve validates sibling keys before diffing. Duplicates here violate
        // the committed snapshot invariant and must not be silently ignored.
        var keyed = new Dictionary<string, Snapshot>(StringComparer.Ordinal);
        foreach (var child in oldChildren)
            if (child.Node.Key is { } key)
                keyed.Add(key, child);

        var used = new HashSet<uint>();
        var next = new Snapshot[children.Count];
        var nextIds = new uint[children.Count];
        for (int i = 0; i < next.Length; ++i)
        {
            var child = (Node)children[i];
            Snapshot? match = null;
            if (child.Key is { } key)
                keyed.TryGetValue(key, out match);
            else if (i < oldChildren.Length && oldChildren[i].Node.Key is null)
                match = oldChildren[i];
            if (match is not null)
                used.Add(match.Id);

            next[i] = Diff(child, match, obsolete, updates);
            nextIds[i] = next[i].Id;
        }

        foreach (var child in oldChildren)
            if (!used.Contains(child.Id))
                obsolete.Add(child);

        bool orderChanged = old is null || oldChildren.Length != next.Length;
        for (int i = 0; !orderChanged && i < next.Length; ++i)
            orderChanged = oldChildren[i].Id != nextIds[i];
        if (orderChanged)
            _buffer.SetChildren(parentId, nextIds);

        return next;
    }

    private uint NextId() => _lastId = checked(_lastId + 1);

    private static string? Label(Node node) =>
        node switch
        {
            Text t => t.Value,
            RadioButton radio => radio.Label,
            NativeNavigationPage page => page.Options.Title,
            NativeHeaderNode header => header.Options.Title,
            Button b => b.Title,
            TextField f => f.Value,
            Icon icon => icon.Name.ToString().ToLowerInvariant(),
            _ => null,
        };

    private static NodeKind Kind(Node node) =>
        node switch
        {
            NativeNavigationNode => NodeKind.Navigation,
            NativeNavigationPage => NodeKind.NavigationPage,
            NativeHeaderNode => NodeKind.NavigationHeader,
            BottomNavigationBar => NodeKind.BottomNavigationBar,
            NativeDesktopNavigation => NodeKind.DesktopNavigation,
            ScrollView => NodeKind.ScrollView,
            TextField => NodeKind.TextField,
            Switch => NodeKind.Switch,
            RadioButton => NodeKind.RadioButton,
            Slider => NodeKind.Slider,
            Image => NodeKind.Image,
            PlatformView => NodeKind.PlatformView,
            Icon => NodeKind.Icon,
            ContainerNode => NodeKind.VStack,
            Text => NodeKind.Text,
            Button => NodeKind.Button,
            _ => throw new NotSupportedException(),
        };

    private void DeleteTree(Snapshot node)
    {
        foreach (var child in node.Children)
            DeleteTree(child);
        _buffer.Delete(node.Id);
    }

    private void Flush()
    {
        if (_buffer.IsEmpty)
            return;
        try
        {
            _transport.Flush(_buffer.WrittenSpan);
        }
        catch (Exception error)
        {
            _faulted = true;
            _transportFailure = error;
            throw;
        }
    }

    private void VerifyThread()
    {
        if (!_dispatcher.CheckAccess())
            throw new InvalidOperationException("Operation requires the OS UI thread.");
    }

    private void VerifyUsable()
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted)
            throw new InvalidOperationException(
                "Transport failed; recreate the native engine session.",
                _transportFailure
            );
        if (_rendering)
            throw new InvalidOperationException("Reentrant rendering/event dispatch is forbidden.");
    }

    public void Dispose()
    {
        VerifyThread();
        if (_disposed)
            return;
        if (_rendering)
            throw new InvalidOperationException("Cannot dispose during rendering.");
        _disposed = true;
        _motionTimer?.Dispose();
        _motionTimer = null;
        _motions.Clear();
        _pendingMotions.Clear();
        _interactionStates.Clear();
        HotReload.Unregister(this);
        if (_resolved is not null)
            ElementResolver.Unmount(_resolved);
        _resolved = null;
        _clicks.Clear();
        _inputs.Clear();
        if (_faulted || _current is null)
            return;
        _buffer.Reset();
        _buffer.SetRoot(0);
        DeleteTree(_current);
        Flush();
        _current = null;
    }
}
