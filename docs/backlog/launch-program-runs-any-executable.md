---
worth: later
where: src/SharpDbg.MCP/Tools/DebuggingTools.cs:144
added: 2026-10-15
---
# launch_program runs any executable

`InputValidation.ValidateProgramPath` checks only that the file exists and is not a project file;
arguments, working directory and environment pass through, so the tool gives an agent arbitrary code
execution. Possible limit: an allow-list of roots (`SHARPDBG_LAUNCH_ROOTS`, defaulting to the working directory).

Unknown that settles it: whether a limit is worth having when an agent with shell access bypasses it
trivially - it only buys anything in shell-less clients.
