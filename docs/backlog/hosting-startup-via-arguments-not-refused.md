---
worth: later
where: src/SharpDbg.MCP/Tools/InputValidation.cs:121
added: 2026-10-18
---
# hosting startup assemblies can still be named in launch arguments

`ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` and `DOTNET_HOSTINGSTARTUPASSEMBLIES` are refused in the launch
environment, but ASP.NET Core reads the same host setting from the command line, so
`--hostingStartupAssemblies=Some.Assembly` in `args` or in a launch profile's `commandLineArgs` makes
the app load and run that assembly's startup. Arguments are not checked at all.

The reach is narrower than the environment variable's: without `DOTNET_ADDITIONAL_DEPS`, which is
refused, only an assembly already among the app's dependencies can be loaded. A check would refuse
the argument in its `--key=value`, `--key value`, `/key` and `key=value` forms, case-insensitively.

Unknown that settles it: whether a dependency an ordinary app ships with carries a hosting startup
that does harm. If none can, loading one only runs code the app already trusts, and this is a `no`.
