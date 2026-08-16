// SharedKernel primitives and the presentation-layer plumbing (ScopedHandler, page
// routes, the localization markup) are used by nearly every file here; importing them
// globally keeps the per-file using blocks about what each file actually does.
global using Helix.App.Common;
global using Helix.App.Localization;
global using SharedKernel.Abstractions;
global using SharedKernel.Primitives;
global using SharedKernel.Results;
