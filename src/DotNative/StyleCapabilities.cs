namespace DotNative;

// Capabilities describe the supported public DSL, not Objective-C inheritance.
// Node deliberately has none: erasing a concrete type also erases its styling API.
public interface ILayoutable { }

public interface IVisualBox : ILayoutable { }

public interface IPaddable : ILayoutable { }

public interface ITextual { }

public interface ITextFlow : ITextual { }

public interface ITintable { }
