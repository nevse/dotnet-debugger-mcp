#!/bin/bash
# Build and test .NET Debugger MCP Server
# Usage: ./scripts/build-and-test.sh

set -e  # Exit on error

echo "=== Building .NET Debugger MCP Server ==="
dotnet build src/DotnetDebugger.Mcp/DotnetDebugger.Mcp.csproj

echo ""
echo "=== Running Tests ==="
dotnet test tests/DotnetDebugger.Mcp.Tests/DotnetDebugger.Mcp.Tests.csproj

echo ""
echo "✅ Build and test complete!"
