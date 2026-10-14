namespace DotNative;

// Keeps mounted descriptions separate from native snapshots. Candidate entries
// acquire subscriptions only after the command transport commits successfully.
internal sealed class ElementResolver(Action schedule)
{
    internal sealed class Entry(Element source, BuildContext context, Action schedule)
    {
        internal readonly Element Source = source;
        internal readonly BuildContext Context = context;
        internal readonly HashSet<IStateDependency> Reads = [];
        internal readonly Dictionary<Type, object?> ContextReads = [];
        internal Entry[] Children = [];
        internal Element? Built;
        internal Node Node = null!;
        internal Entry? Parent;
        internal bool DirtySelf;
        internal bool DirtyBranch;
        internal bool Mounted;
        internal int Height;

        // The callback must target this mounted occurrence, not merely its
        // Component instance: one instance can appear in separate tree scopes.
        internal void Changed()
        {
            if (!Mounted)
                return;
            DirtySelf = true;
            for (Entry? entry = this; entry is not null; entry = entry.Parent)
                entry.DirtyBranch = true;
            schedule();
        }
    }

    private readonly List<Entry> _created = [];
    private readonly List<Entry> _replaced = [];
    private readonly List<Entry> _removed = [];
    private readonly HashSet<Element> _path = new(ReferenceEqualityComparer.Instance);
    private bool _force;
    internal int Visited { get; private set; }

    internal Entry Resolve(Element root, Entry? old, BuildContext context, bool force)
    {
        _created.Clear();
        _replaced.Clear();
        _removed.Clear();
        _path.Clear();
        _force = force;
        Visited = 0;
        return Visit(root, old, context, 0);
    }

    private Entry Visit(Element element, Entry? old, BuildContext context, int depth)
    {
        Visited++;
        if (depth > 256)
            throw new InvalidOperationException("UI depth exceeds 256.");
        if (!_path.Add(element))
            throw new InvalidOperationException("Cyclic component/tree.");
        var previousReads = BuildScope.Reads;
        var previousContextReads = BuildScope.ContextReads;
        try
        {
            bool sameSource = old is not null && ReferenceEquals(element, old.Source);
            if (sameSource && !_force && !old!.DirtyBranch && context.SameValues(old.Context))
            {
                if (depth + old.Height > 256)
                    throw new InvalidOperationException("UI depth exceeds 256.");
                return old;
            }

            var next = new Entry(element, context, schedule);
            _created.Add(next);
            if (old is not null)
                _replaced.Add(old);
            BuildScope.Reads = next.Reads;
            BuildScope.ContextReads = next.ContextReads;

            if (element is Component component)
            {
                bool rebuild =
                    !sameSource || _force || old!.DirtySelf || !context.Matches(old.ContextReads);
                if (rebuild)
                    next.Built =
                        component.Build(context)
                        ?? throw new InvalidOperationException("Build returned null.");
                else
                {
                    next.Built = old!.Built;
                    next.Reads.UnionWith(old.Reads);
                    foreach (var read in old.ContextReads)
                        next.ContextReads.Add(read.Key, read.Value);
                }
                next.Children = [Visit(next.Built!, SingleChild(old), context, depth + 1)];
                next.Node = next.Children[0].Node;
            }
            else if (element is IContextScope scope)
            {
                next.Children =
                [
                    Visit(scope.Child, SingleChild(old), scope.Apply(context), depth + 1),
                ];
                next.Node = next.Children[0].Node;
            }
            else if (element is FillNavigationContent fill)
            {
                next.Children = [Visit(fill.Child, SingleChild(old), context, depth + 1)];
                var content = next.Children[0].Node;
                next.Node = content with
                {
                    Key = fill.Key,
                    Layout = content.Layout with { Height = 0, FlexGrow = 1 },
                };
            }
            else if (element is ContainerNode container)
            {
                var prior = old?.Children ?? [];
                var used = new HashSet<Entry>();
                var byIdentity = new Dictionary<Element, Queue<Entry>>(
                    ReferenceEqualityComparer.Instance
                );
                foreach (var child in prior)
                {
                    if (!byIdentity.TryGetValue(child.Source, out var occurrences))
                        byIdentity.Add(child.Source, occurrences = new());
                    occurrences.Enqueue(child);
                }
                next.Children = new Entry[container.Children.Count];
                var nodes = new Element[next.Children.Length];
                var keys = new HashSet<string>(StringComparer.Ordinal);
                var matches = new Entry?[next.Children.Length];
                // Reserve retained instances before positional matching. An
                // insertion must not consume the old entry of a later sibling.
                for (int i = 0; i < next.Children.Length; i++)
                {
                    var source = container.Children[i];
                    if (byIdentity.TryGetValue(source, out var occurrences))
                        if (occurrences.TryDequeue(out var candidate))
                        {
                            matches[i] = candidate;
                            used.Add(candidate);
                        }
                }
                for (int i = 0; i < next.Children.Length; i++)
                {
                    var source = container.Children[i];
                    var match = matches[i];
                    if (match is null && i < prior.Length && !used.Contains(prior[i]))
                        match = prior[i];
                    if (match is not null)
                        used.Add(match);
                    var child = Visit(source, match, context, depth + 1);
                    next.Children[i] = child;
                    nodes[i] = child.Node;
                    if (child.Node.Key is { } key && !keys.Add(key))
                        throw new InvalidOperationException($"Duplicate sibling key: {key}");
                }
                foreach (var child in prior)
                    if (!used.Contains(child))
                        _removed.Add(child);
                next.Node = container with
                {
                    Children = Array.AsReadOnly(nodes),
                    ResolvedControl = RenderHost.Control(container),
                };
            }
            else if (element is Node node)
            {
                if (old is not null)
                    _removed.AddRange(old.Children);
                var theme = Theme.Of(context);
                next.Node = node with
                {
                    ResolvedControl = RenderHost.Control(node),
                    Style = node.Style with
                    {
                        Foreground = node.HasForegroundOverride
                            ? node.Style.Foreground
                            : theme.Foreground,
                        FontSize = node.HasFontSizeOverride ? node.Style.FontSize : theme.FontSize,
                    },
                };
            }
            else
                throw new NotSupportedException($"Unsupported element: {element.GetType().Name}");
            next.Height =
                next.Children.Length == 0 ? 0 : next.Children.Max(child => child.Height) + 1;
            return next;
        }
        finally
        {
            BuildScope.Reads = previousReads;
            BuildScope.ContextReads = previousContextReads;
            _path.Remove(element);
        }
    }

    private Entry? SingleChild(Entry? old)
    {
        if (old is null || old.Children.Length == 0)
            return null;
        for (int i = 1; i < old.Children.Length; i++)
            _removed.Add(old.Children[i]);
        return old.Children[0];
    }

    internal void Commit()
    {
        foreach (var removed in _removed)
            Unmount(removed);
        foreach (var replaced in _replaced)
            Unsubscribe(replaced);
        foreach (var entry in _created)
        {
            entry.Mounted = true;
            foreach (var child in entry.Children)
                child.Parent = entry;
            foreach (var read in entry.Reads)
                read.Subscribe(entry.Changed);
        }
        Clear();
    }

    internal void Clear()
    {
        _created.Clear();
        _replaced.Clear();
        _removed.Clear();
    }

    internal static void Unmount(Entry entry)
    {
        Unsubscribe(entry);
        foreach (var child in entry.Children)
            Unmount(child);
    }

    private static void Unsubscribe(Entry entry)
    {
        entry.Mounted = false;
        foreach (var read in entry.Reads)
            read.Unsubscribe(entry.Changed);
    }
}
