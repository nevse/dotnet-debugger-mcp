---
worth: yes
where: src/SharpDbg.MCP/Tools/DebuggingTools.cs:144
added: 2026-10-18
---
# launch_program cannot use a project's launchSettings.json profile

An ASP.NET app usually needs what its `Properties/launchSettings.json` profile sets -
`ASPNETCORE_ENVIRONMENT`, `applicationUrl`, arguments - so today the agent has to rebuild that by hand
in `args` and `environment`.

clrdbg can apply a profile itself (`launchSettingsFilePath`, `launchSettingsProfile`; since f4b48ef it
ignores `executablePath`, so a profile cannot change the program). Do not pass those through: clrdbg
applies the profile after this server has checked the request and replaces the environment with the
profile's, so a hostile repository's profile would bypass the refused-variable check on `environment`.
The file lives in the repository, so it is no more trusted than the agent's own input.

Instead, read the profile here, with clrdbg's selection rules (the named profile, else `https`, else the
first with `commandName: "Project"`), turn it into `args`, `cwd` and `env` - `applicationUrl` becoming
`ASPNETCORE_URLS` - and run that through the same validation as the agent's parameters. Finding the file
needs the project, not the built `.dll`: take a `project_path` with `launch_profile` rather than walking
up from `bin/`, which breaks on any custom `OutputPath`.
