namespace DotNative;

// A regular auto-sized layout container. Only committed native geometry is reported.
internal sealed record MeasuredItem(Element child, uint Revision, Action<float, float> Measured)
    : ContainerNode(child);
