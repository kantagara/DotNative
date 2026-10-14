# Optional platform code

Shared C# components live at the project root. Native platform code lives under
iOS/, Android/, Mac/, Windows/ and Linux/. Use DOTNATIVE_IOS, DOTNATIVE_ANDROID,
DOTNATIVE_MACOS, DOTNATIVE_WINDOWS, DOTNATIVE_LINUX and DOTNATIVE_SIMULATOR for
conditional branches.

Windows/Linux currently support repository previews with controls, scrolling and
navigation. Their managed preview host selects the matching platform C# files.
Native plugin handlers and standalone release packaging remain unfinished.
See docs/desktop-preview.md in the framework repository.
