using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(DotNative.HotReload))]

namespace DotNative;

// Called by a managed development host after supported code deltas are applied.
// This hook itself cannot patch code in a NativeAOT binary.
public static class HotReload
{
    private static readonly object Gate = new();
    private static readonly List<WeakReference<RenderHost>> Hosts = [];

    internal static void Register(RenderHost host)
    {
        lock (Gate)
        {
            Hosts.RemoveAll(x => !x.TryGetTarget(out _));
            Hosts.Add(new(host));
        }
    }

    internal static void Unregister(RenderHost host)
    {
        lock (Gate)
            Hosts.RemoveAll(x => !x.TryGetTarget(out var target) || ReferenceEquals(target, host));
    }

    public static void UpdateApplication(Type[]? updatedTypes)
    {
        RenderHost[] live;
        lock (Gate)
        {
            var targets = new List<RenderHost>();
            foreach (var reference in Hosts)
                if (reference.TryGetTarget(out var host))
                    targets.Add(host);
            live = targets.ToArray();
        }
        foreach (var host in live)
            host.RequestRebuild();
    }
}
