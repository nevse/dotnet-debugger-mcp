# Contributing to DotnetDebugger.Mcp

Thank you for your interest in contributing to DotnetDebugger.Mcp! This document provides guidelines and information for contributors.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Project Structure](#project-structure)
- [Coding Standards](#coding-standards)
- [Adding New Features](#adding-new-features)
- [Testing](#testing)
- [Pull Request Process](#pull-request-process)
- [Reporting Issues](#reporting-issues)

## Code of Conduct

This project adheres to a code of professionalism and respect. All contributors are expected to:

- Be respectful and considerate in communications
- Welcome newcomers and help them get started
- Focus on constructive feedback
- Accept responsibility for mistakes and learn from them

## Getting Started

1. **Fork the repository** on GitHub
2. **Clone your fork** locally:
   ```bash
   git clone https://github.com/YOUR_USERNAME/dotnet-debugger-mcp.git
   cd dotnet-debugger-mcp
   ```
3. **Add upstream remote**:
   ```bash
   git remote add upstream https://github.com/nevse/dotnet-debugger-mcp.git
   ```

## Development Setup

### Prerequisites

- .NET 10 SDK or later
- Git
- A code editor (VS Code, Visual Studio, Rider recommended)

### Building the Project

The debugger this server drives is [clrdbg](https://github.com/JaneySprings/clrdbg), taken from one of
its releases: `ClrdbgVersion` in `Directory.Build.props` pins it, with a SHA-256 for each zip. The
`FetchClrdbgRelease` target in `Directory.Build.targets` downloads its debug adapter, checks the hash
and unpacks it, and `CopyClrdbgAdapter` puts it in a `clrdbg/` folder beside the build output. This runs
as part of `dotnet build`, so there is no separate step, and the first build needs network access to
github.com.

At run time the server starts the adapter as a child process, `dotnet <output directory>/clrdbg/clrdbg.dll`,
and speaks the Debug Adapter Protocol over its standard input and output. That code lives in
`src/DotnetDebugger.Mcp/Debugging/ChildProcessDebugAdapter.cs`. A process of its own is what makes a native
crash inside `libmscordbi` take down the adapter rather than the server, and on macOS the `dotnet`
muxer carries the entitlement a debugger needs. Set `DOTNET_DEBUGGER_MCP_ADAPTER_PATH` to the adapter assembly to
use one built elsewhere.

```bash
# Build the main project
dotnet build src/DotnetDebugger.Mcp/DotnetDebugger.Mcp.csproj

# Run tests
dotnet test tests/DotnetDebugger.Mcp.Tests/DotnetDebugger.Mcp.Tests.csproj

# Or use the helper script
./scripts/build-and-test.sh
```

### Building Against a Local clrdbg Checkout

When a fix belongs in the debugger rather than in this server, point the build at a clrdbg checkout of
your own with the `ClrdbgSourcePath` property, and the adapter is built from it in place of the
release's. The remote CoreCLR libraries still come from the release, so the checkout has to speak their
protocol. An environment variable of the same name sets it for every command in a shell:

```bash
export ClrdbgSourcePath=/path/to/clrdbg
dotnet build
```

For a single command:

```bash
dotnet build -p:ClrdbgSourcePath=/path/to/clrdbg
```

### Running the Server

```bash
# Run with default configuration
dotnet run --project src/DotnetDebugger.Mcp/DotnetDebugger.Mcp.csproj

# Run with debug logging
export DOTNET_DEBUGGER_MCP_LOG_LEVEL="Debug"
dotnet run --project src/DotnetDebugger.Mcp/DotnetDebugger.Mcp.csproj

# Or use the helper script
./scripts/test-server.sh
```

## Project Structure

```
dotnet-debugger-mcp/
├── src/
│   └── DotnetDebugger.Mcp/      # Main project
│       ├── Program.cs           # Entry point
│       ├── Tools/               # MCP tool implementations
│       ├── Debugging/           # Debugger session management
│       ├── Documentation/       # Documentation indexing
│       ├── Logging/             # Logging infrastructure
│       ├── Configuration/       # Configuration system
│       └── Data/                # Embedded resources
├── tests/
│   └── DotnetDebugger.Mcp.Tests/ # Unit tests
├── examples/                    # Extension examples
├── scripts/                     # Development scripts
└── .github/workflows/           # CI/CD automation
```

## Coding Standards

### General Guidelines

- **No Regions**: Do not use `#region` directives in C# code
- **Latest Packages**: Always use the latest stable NuGet package versions
- **Nullable Reference Types**: Enable and properly handle nullable annotations
- **Async/Await**: Use async patterns for I/O operations
- **Thread Safety**: Use locks for shared state (see DebugSession.cs for examples)

### Naming Conventions

- **Classes**: PascalCase (e.g., `DebugSession`)
- **Methods**: PascalCase (e.g., `AttachToProcess`)
- **Private Fields**: _camelCase with underscore prefix (e.g., `_debugger`)
- **Parameters**: camelCase (e.g., `processId`)
- **Constants**: PascalCase (e.g., `DefaultTimeout`)

### Code Organization

- **Single Responsibility**: Each class should have one clear purpose
- **Small Methods**: Keep methods focused and under 50 lines when possible
- **XML Documentation**: Add XML comments for public APIs
- **Error Handling**: Use try-catch at boundaries, let exceptions propagate internally

### Example

```csharp
/// <summary>
/// Attaches the debugger to a process
/// </summary>
/// <param name="processId">Process ID to attach to</param>
/// <exception cref="ArgumentException">Thrown when processId is invalid</exception>
/// <exception cref="InvalidOperationException">Thrown when already attached</exception>
public void Attach(int processId)
{
    // Validate input
    if (processId <= 0)
        throw new ArgumentException($"Process ID must be positive, got: {processId}", nameof(processId));

    lock (_stateLock)
    {
        if (_attached)
            throw new InvalidOperationException("Already attached to a process");

        _debugger = new DapDebugger(LogMessage);
        _debugger.Attach(processId, justMyCode, timeout);
        _attached = true;
    }
}
```

## Adding New Features

### Adding a New MCP Tool

1. **Add the method** to the appropriate tools class (McpTools.cs or DebuggingTools.cs)
2. **Decorate with attributes**:
   ```csharp
   [McpServerTool(ReadOnly = true, OpenWorld = false), Description("Your tool description")]
   public static string YourToolName(string param1, int param2)
   {
       // Implementation
   }
   ```
3. **Validate inputs** using InputValidation class
4. **Return JSON** using System.Text.Json.JsonSerializer
5. **Add tests** in DotnetDebugger.Mcp.Tests
6. **Declare annotations** that match what the tool does: `ReadOnly` for a read that runs no debuggee
   code, `Destructive = false` for one that changes only the session, and `Destructive = true,
   OpenWorld = true` for one that runs code the agent chose - an expression argument counts
7. **Document in README** in the "Tools" section, with its Annotation column. A tool that runs code
   also goes in the list of calls to confirm under "What `launch_program` will run"

### Adding New Configuration Options

1. **Add property** to ServerConfiguration.cs
2. **Add environment variable loading** in LoadFromEnvironment()
3. **Add validation** in Validate()
4. **Document** in README Configuration section
5. **Add tests** in ServerConfigurationTests.cs

### Extending Documentation

1. **Edit** Data/how_dotnet_debuggers_work.md
2. **Rebuild** to update embedded resource
3. **Test search** using search_debugging_concepts tool

## Testing

### Writing Tests

- Use MSTest framework
- Follow Arrange-Act-Assert pattern
- Test happy paths and error cases
- Mock external dependencies when possible

### Example Test

```csharp
[TestMethod]
public void ValidateProcessId_ValidId_DoesNotThrow()
{
    // Arrange & Act & Assert
    InputValidation.ValidateProcessId(1);
    InputValidation.ValidateProcessId(12345);
}

[TestMethod]
public void ValidateProcessId_InvalidId_ThrowsException()
{
    // Arrange, Act & Assert
    try
    {
        InputValidation.ValidateProcessId(0);
        Assert.Fail("Expected ArgumentException");
    }
    catch (ArgumentException)
    {
        // Expected
    }
}
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~InputValidationTests"

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"
```

## Pull Request Process

1. **Create a feature branch**:
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Make your changes**:
   - Write code following coding standards
   - Add tests for new functionality
   - Update documentation (README, XML comments)
   - Ensure all tests pass

3. **Commit your changes**:
   ```bash
   git add .
   git commit -m "feat: Add your feature description"
   ```

   **Commit Message Format**:
   - `feat:` - New feature
   - `fix:` - Bug fix
   - `docs:` - Documentation changes
   - `test:` - Test changes
   - `refactor:` - Code refactoring
   - `perf:` - Performance improvements

4. **Push to your fork**:
   ```bash
   git push origin feature/your-feature-name
   ```

5. **Create a Pull Request**:
   - Go to GitHub and create a PR from your branch
   - Fill out the PR template (if provided)
   - Link any related issues
   - Request review from maintainers

6. **Address Feedback**:
   - Respond to review comments
   - Make requested changes
   - Push additional commits to your branch

7. **Merge**:
   - Once approved, a maintainer will merge your PR
   - Delete your feature branch after merge

### PR Checklist

Before submitting, ensure:

- [ ] Code follows project coding standards
- [ ] All tests pass locally
- [ ] New tests added for new functionality
- [ ] Documentation updated (README, XML comments)
- [ ] No unnecessary changes (whitespace, formatting in unrelated files)
- [ ] Commit messages are clear and descriptive
- [ ] PR description explains what and why

## Reporting Issues

### Bug Reports

When reporting a bug, include:

1. **Description**: Clear description of the issue
2. **Steps to Reproduce**: Numbered list of steps
3. **Expected Behavior**: What should happen
4. **Actual Behavior**: What actually happens
5. **Environment**:
   - OS (Windows/macOS/Linux + version)
   - .NET SDK version (`dotnet --version`)
   - DotnetDebugger.Mcp package version
6. **Logs**: Relevant error messages or stack traces
7. **Configuration**: Environment variables used

### Feature Requests

When requesting a feature:

1. **Use Case**: Describe the problem you're trying to solve
2. **Proposed Solution**: Your idea for solving it
3. **Alternatives**: Other approaches you've considered
4. **Additional Context**: Screenshots, examples, links

### Security Issues

**Do not open public issues for security vulnerabilities.**

Instead, email security details to: [SECURITY_EMAIL]

## Development Tips

### Debugging the MCP Server

1. **Run with verbose logging**:
   ```bash
   export DOTNET_DEBUGGER_MCP_LOG_LEVEL="Trace"
   export DOTNET_DEBUGGER_MCP_ENABLE_DIAGNOSTICS="true"
   dotnet run
   ```

2. **Test with TestApp**:
   ```bash
   # Terminal 1: Run TestApp
   dotnet run --project tests/DotnetDebugger.Mcp.TestApp

   # Terminal 2: Run MCP server and attach to TestApp
   dotnet run --project src/DotnetDebugger.Mcp
   ```

3. **Use breakpoints**: Attach a debugger to the MCP server process itself

### Common Pitfalls

- **Forgetting to validate inputs**: Always use InputValidation class
- **Blocking async operations**: Don't call `.Result` or `.Wait()` unnecessarily
- **Not handling disposal**: Implement IDisposable properly for resources
- **Ignoring thread safety**: Use locks for shared mutable state

### Helpful Commands

```bash
# Clean build artifacts
dotnet clean

# Restore packages
dotnet restore

# Format code (if using dotnet-format)
dotnet format

# List outdated packages
dotnet list package --outdated
```

## Questions?

- Check existing issues and discussions
- Read the README and documentation
- Ask in GitHub Discussions
- Reach out to maintainers

Thank you for contributing to DotnetDebugger.Mcp!
