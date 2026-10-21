#!/bin/bash
# Run the test application for debugging
# Usage: ./scripts/run-test-app.sh

echo "=== Building TestApp ==="
cd "$(dirname "$0")/../tests/DotnetDebugger.Mcp.TestApp"
dotnet build

echo ""
echo "=== Running TestApp (Process ID will be shown) ==="
echo "Attach debugger to this process using .NET Debugger MCP Server"
echo ""

dotnet run
