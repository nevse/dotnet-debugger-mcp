#!/bin/bash
# Manually test the MCP server with debug logging
# Usage: ./scripts/test-server.sh

echo "=== Starting .NET Debugger MCP Server (Debug Mode) ==="
echo "Press Ctrl+C to stop"
echo ""

export DOTNET_DEBUGGER_MCP_LOG_LEVEL="Debug"
export DOTNET_DEBUGGER_MCP_ENABLE_DIAGNOSTICS="true"

dotnet run --project src/DotnetDebugger.Mcp/DotnetDebugger.Mcp.csproj
