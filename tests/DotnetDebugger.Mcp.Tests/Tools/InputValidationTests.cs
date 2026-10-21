using DotnetDebugger.Mcp.Debugging;
using DotnetDebugger.Mcp.Tools;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DotnetDebugger.Mcp.Tests.Tools;

[TestClass]
public class InputValidationTests
{
    [TestMethod]
    public void ValidateProcessId_ValidId_DoesNotThrow()
    {
        InputValidation.ValidateProcessId(1);
        InputValidation.ValidateProcessId(12345);
        InputValidation.ValidateProcessId(int.MaxValue);
    }

    [TestMethod]
    public void ValidateProcessId_InvalidIds_ThrowsArgumentException()
    {
        try
        {
            InputValidation.ValidateProcessId(0);
            Assert.Fail("Expected ArgumentException for processId 0");
        }
        catch (ArgumentException) { }

        try
        {
            InputValidation.ValidateProcessId(-1);
            Assert.Fail("Expected ArgumentException for negative processId");
        }
        catch (ArgumentException) { }
    }

    [TestMethod]
    public void ValidateThreadId_ValidId_DoesNotThrow()
    {
        InputValidation.ValidateThreadId(0);
        InputValidation.ValidateThreadId(1);
        InputValidation.ValidateThreadId(12345);
    }

    [TestMethod]
    public void ValidateThreadId_Negative_ThrowsArgumentException()
    {
        try
        {
            InputValidation.ValidateThreadId(-1);
            Assert.Fail("Expected ArgumentException for negative threadId");
        }
        catch (ArgumentException) { }
    }

    [TestMethod]
    public void ValidateFrameId_ValidId_DoesNotThrow()
    {
        InputValidation.ValidateFrameId(1);
        InputValidation.ValidateFrameId(12345);
    }

    [TestMethod]
    public void ValidateFrameId_InvalidIds_ThrowsArgumentException()
    {
        try
        {
            InputValidation.ValidateFrameId(0);
            Assert.Fail("Expected ArgumentException for frameId 0");
        }
        catch (ArgumentException) { }

        try
        {
            InputValidation.ValidateFrameId(-1);
            Assert.Fail("Expected ArgumentException for negative frameId");
        }
        catch (ArgumentException) { }
    }

    [TestMethod]
    public void ValidateFilePath_ValidPath_DoesNotThrow()
    {
        InputValidation.ValidateFilePath("/path/to/file.cs");
        InputValidation.ValidateFilePath("C:\\path\\to\\file.cs");
        InputValidation.ValidateFilePath("relative/path.cs");
    }

    [TestMethod]
    public void ValidateFilePath_InvalidPaths_ThrowsArgumentException()
    {
        try
        {
            InputValidation.ValidateFilePath(null!);
            Assert.Fail("Expected ArgumentException for null filePath");
        }
        catch (ArgumentException) { }

        try
        {
            InputValidation.ValidateFilePath("");
            Assert.Fail("Expected ArgumentException for empty filePath");
        }
        catch (ArgumentException) { }

        try
        {
            InputValidation.ValidateFilePath("   ");
            Assert.Fail("Expected ArgumentException for whitespace filePath");
        }
        catch (ArgumentException) { }
    }

    [TestMethod]
    public void ValidateLineNumber_ValidNumber_DoesNotThrow()
    {
        InputValidation.ValidateLineNumber(1);
        InputValidation.ValidateLineNumber(42);
        InputValidation.ValidateLineNumber(int.MaxValue);
    }

    [TestMethod]
    public void ValidateLineNumber_InvalidNumbers_ThrowsArgumentException()
    {
        try
        {
            InputValidation.ValidateLineNumber(0);
            Assert.Fail("Expected ArgumentException for lineNumber 0");
        }
        catch (ArgumentException) { }

        try
        {
            InputValidation.ValidateLineNumber(-1);
            Assert.Fail("Expected ArgumentException for negative lineNumber");
        }
        catch (ArgumentException) { }
    }

    [TestMethod]
    public void ValidateBreakpointId_ValidId_DoesNotThrow()
    {
        InputValidation.ValidateBreakpointId(1);
        InputValidation.ValidateBreakpointId(int.MaxValue);
    }

    [TestMethod]
    public void ValidateBreakpointId_InvalidIds_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => InputValidation.ValidateBreakpointId(0));
        Assert.ThrowsExactly<ArgumentException>(() => InputValidation.ValidateBreakpointId(-1));
    }

    [TestMethod]
    public void ValidateHitCondition_ValidForms_DoNotThrow()
    {
        InputValidation.ValidateHitCondition(null);
        InputValidation.ValidateHitCondition("");
        InputValidation.ValidateHitCondition("5");
        InputValidation.ValidateHitCondition("==5");
        InputValidation.ValidateHitCondition(">5");
        InputValidation.ValidateHitCondition(">=5");
        InputValidation.ValidateHitCondition("<5");
        InputValidation.ValidateHitCondition("<=5");
        InputValidation.ValidateHitCondition("%5");
        InputValidation.ValidateHitCondition("  >= 5 ".Replace(" ", string.Empty));
    }

    [TestMethod]
    public void ValidateHitCondition_Unparseable_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => InputValidation.ValidateHitCondition("every other"));
        Assert.ThrowsExactly<ArgumentException>(() => InputValidation.ValidateHitCondition(">"));
        Assert.ThrowsExactly<ArgumentException>(() => InputValidation.ValidateHitCondition("=5"));
        Assert.ThrowsExactly<ArgumentException>(() => InputValidation.ValidateHitCondition("5x"));
    }

    [TestMethod]
    public void ValidateHitCondition_ModuloWithoutPositiveInterval_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => InputValidation.ValidateHitCondition("%0"));
        Assert.ThrowsExactly<ArgumentException>(() => InputValidation.ValidateHitCondition("%-2"));
    }

    [TestMethod]
    public void ValidateExpression_ValidExpression_DoesNotThrow()
    {
        InputValidation.ValidateExpression("user.Name");
        InputValidation.ValidateExpression("x + y");
        InputValidation.ValidateExpression("obj.ToString()");
    }

    [TestMethod]
    public void ValidateExpression_InvalidExpressions_ThrowsArgumentException()
    {
        try
        {
            InputValidation.ValidateExpression(null!);
            Assert.Fail("Expected ArgumentException for null expression");
        }
        catch (ArgumentException) { }

        try
        {
            InputValidation.ValidateExpression("");
            Assert.Fail("Expected ArgumentException for empty expression");
        }
        catch (ArgumentException) { }

        try
        {
            InputValidation.ValidateExpression("   ");
            Assert.Fail("Expected ArgumentException for whitespace expression");
        }
        catch (ArgumentException) { }
    }

    [TestMethod]
    public void ParseExceptionBreakMode_KnownModes_ParsesCaseInsensitively()
    {
        Assert.AreEqual(ExceptionBreakMode.Always, InputValidation.ParseExceptionBreakMode("always"));
        Assert.AreEqual(ExceptionBreakMode.Always, InputValidation.ParseExceptionBreakMode(" Always "));
        Assert.AreEqual(ExceptionBreakMode.Never, InputValidation.ParseExceptionBreakMode("NEVER"));
    }

    [TestMethod]
    public void ParseExceptionBreakMode_UnknownMode_ThrowsInsteadOfDefaulting()
    {
        // Falling back to a default would silently change whether the debuggee stops
        foreach (var mode in new[] { "", "  ", "sometimes", "user-unhandled", "unhandled only" })
        {
            try
            {
                InputValidation.ParseExceptionBreakMode(mode);
                Assert.Fail($"Expected ArgumentException for mode '{mode}'");
            }
            catch (ArgumentException) { }
        }
    }

    /// <summary>
    /// The four the debugger can actually distinguish. 'unhandled' was refused here until the move off
    /// SharpDbg, whose stops carried no way to tell a handled exception from one that kills the program.
    /// </summary>
    [TestMethod]
    public void ParseExceptionBreakMode_AcceptsEveryModeTheDebuggerSupports()
    {
        Assert.AreEqual(ExceptionBreakMode.Always, InputValidation.ParseExceptionBreakMode("always"));
        Assert.AreEqual(ExceptionBreakMode.UserUnhandled, InputValidation.ParseExceptionBreakMode("user_unhandled"));
        Assert.AreEqual(ExceptionBreakMode.Unhandled, InputValidation.ParseExceptionBreakMode("unhandled"));
        Assert.AreEqual(ExceptionBreakMode.Never, InputValidation.ParseExceptionBreakMode("never"));

        // Case and padding are the caller's, not ours
        Assert.AreEqual(ExceptionBreakMode.UserUnhandled, InputValidation.ParseExceptionBreakMode("  USER_Unhandled "));
    }

    [TestMethod]
    public void ValidateWaitTimeout_ValidTimeouts_DoesNotThrow()
    {
        InputValidation.ValidateWaitTimeout(1);
        InputValidation.ValidateWaitTimeout(10_000);
        InputValidation.ValidateWaitTimeout(300_000);
    }

    [TestMethod]
    public void ValidateWaitTimeout_InvalidTimeouts_ThrowsArgumentException()
    {
        try
        {
            InputValidation.ValidateWaitTimeout(0);
            Assert.Fail("Expected ArgumentException for timeout 0");
        }
        catch (ArgumentException) { }

        try
        {
            InputValidation.ValidateWaitTimeout(-1);
            Assert.Fail("Expected ArgumentException for negative timeout");
        }
        catch (ArgumentException) { }

        // A wait longer than the client's own request timeout would leave the caller with nothing
        try
        {
            InputValidation.ValidateWaitTimeout(300_001);
            Assert.Fail("Expected ArgumentException for a timeout past the maximum");
        }
        catch (ArgumentException) { }
    }

    /// <summary>
    /// A directory laid out as build output: the files named are created empty, since only their
    /// presence is checked
    /// </summary>
    private static string BuildOutput(params string[] files)
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"launch-{Guid.NewGuid():N}")).FullName;

        foreach (var file in files)
            File.WriteAllText(Path.Combine(directory, file), string.Empty);

        return directory;
    }

    private static string AppHostName => OperatingSystem.IsWindows() ? "App.exe" : "App";

    [TestMethod]
    public void ValidateProgramPath_AppAssembly_ReturnsAnAbsolutePath()
    {
        var directory = BuildOutput("App.dll", "App.runtimeconfig.json");

        try
        {
            var validated = InputValidation.ValidateProgramPath(Path.Combine(directory, "App.dll"));

            Assert.IsTrue(Path.IsPathRooted(validated));
            Assert.IsTrue(File.Exists(validated));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The apphost of a framework-dependent build and of a self-contained publish both sit beside
    /// the app's .dll and .runtimeconfig.json, which is what makes them a .NET program
    /// </summary>
    [TestMethod]
    public void ValidateProgramPath_AppHost_IsAccepted()
    {
        var directory = BuildOutput(AppHostName, "App.dll", "App.runtimeconfig.json");

        try
        {
            var program = Path.Combine(directory, AppHostName);

            Assert.AreEqual(program, InputValidation.ValidateProgramPath(program));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A library has no runtime configuration and cannot be run, and a .dll that is not build
    /// output at all is refused the same way
    /// </summary>
    [TestMethod]
    public void ValidateProgramPath_AssemblyWithoutRuntimeConfig_IsRefused()
    {
        var directory = BuildOutput("Library.dll");

        try
        {
            var error = Assert.Throws<ArgumentException>(
                () => InputValidation.ValidateProgramPath(Path.Combine(directory, "Library.dll")));

            StringAssert.Contains(error.Message, "Library.runtimeconfig.json");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The reason for the check: an executable that is not a .NET app - a shell, curl, anything -
    /// would otherwise be started by the debugger with whatever arguments it was given
    /// </summary>
    [TestMethod]
    public void ValidateProgramPath_NativeExecutable_IsRefused()
    {
        var shell = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
            : "/bin/sh";

        var error = Assert.Throws<ArgumentException>(() => InputValidation.ValidateProgramPath(shell));

        StringAssert.Contains(error.Message, "only .NET programs are launched");
    }

    /// <summary>
    /// A single-file publish leaves only the apphost, with the app packed inside it. It cannot be
    /// debugged, so refusing it up front says so rather than failing inside the debugger
    /// </summary>
    [TestMethod]
    public void ValidateProgramPath_AppHostAlone_IsRefusedAndNamesSingleFile()
    {
        var directory = BuildOutput(AppHostName);

        try
        {
            var error = Assert.Throws<ArgumentException>(
                () => InputValidation.ValidateProgramPath(Path.Combine(directory, AppHostName)));

            StringAssert.Contains(error.Message, "single-file");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Copying a native executable next to a .dll is not enough: the files beside it must carry the
    /// executable's own name
    /// </summary>
    [TestMethod]
    public void ValidateProgramPath_ExecutableBesideSomeoneElsesApp_IsRefused()
    {
        var other = OperatingSystem.IsWindows() ? "tool.exe" : "tool";
        var directory = BuildOutput(other, "App.dll", "App.runtimeconfig.json");

        try
        {
            Assert.Throws<ArgumentException>(
                () => InputValidation.ValidateProgramPath(Path.Combine(directory, other)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("DOTNET_STARTUP_HOOKS")]
    [DataRow("DOTNET_ADDITIONAL_DEPS")]
    [DataRow("DOTNET_SHARED_STORE")]
    [DataRow("CORECLR_ENABLE_PROFILING")]
    [DataRow("CORECLR_PROFILER")]
    [DataRow("CORECLR_PROFILER_PATH_64")]
    [DataRow("COR_PROFILER_PATH")]
    [DataRow("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES")]
    [DataRow("DOTNET_HOSTINGSTARTUPASSEMBLIES")]
    [DataRow("DOTNET_ROOT")]
    [DataRow("DOTNET_ROOT_X64")]
    [DataRow("LD_PRELOAD")]
    [DataRow("LD_LIBRARY_PATH")]
    [DataRow("DYLD_INSERT_LIBRARIES")]
    [DataRow("dotnet_startup_hooks")]
    public void ValidateLaunchEnvironment_CodeInjectingVariable_IsRefused(string name)
    {
        var environment = new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Development", [name] = "x" };

        var error = Assert.Throws<ArgumentException>(() => InputValidation.ValidateLaunchEnvironment(environment));

        StringAssert.Contains(error.Message, name);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", error.Message);
    }

    /// <summary>
    /// What a program is ordinarily configured with passes, including variables that only share a
    /// word with a refused one
    /// </summary>
    [TestMethod]
    public void ValidateLaunchEnvironment_OrdinaryVariables_Pass()
    {
        InputValidation.ValidateLaunchEnvironment(null);
        InputValidation.ValidateLaunchEnvironment(new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = "http://localhost:5000",
            ["DOTNET_ENVIRONMENT"] = "Staging",
            ["ConnectionStrings__Default"] = "Server=.",
            ["PATH"] = "/usr/bin"
        });
    }

    [TestMethod]
    public void ValidateProgramPath_MissingFile_SaysWhereItLooked()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.dll");

        var error = Assert.Throws<ArgumentException>(() => InputValidation.ValidateProgramPath(missing));

        StringAssert.Contains(error.Message, missing);
    }

    /// <summary>
    /// Handing over a project rather than its build output is the mistake worth naming: the debugger
    /// would try to run the .csproj and fail with something that explains nothing.
    /// </summary>
    [TestMethod]
    public void ValidateProgramPath_ProjectFile_SaysToBuildItFirst()
    {
        var error = Assert.Throws<ArgumentException>(
            () => InputValidation.ValidateProgramPath("/somewhere/MyApp.csproj"));

        StringAssert.Contains(error.Message, "Build it");
    }

    [TestMethod]
    public void ValidateProgramPath_Empty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => InputValidation.ValidateProgramPath(""));
        Assert.Throws<ArgumentException>(() => InputValidation.ValidateProgramPath("   "));
    }

    [TestMethod]
    public void ValidateWorkingDirectory_NullMeansTheProgramsOwnDirectory()
    {
        InputValidation.ValidateWorkingDirectory(null);
    }

    [TestMethod]
    public void ValidateWorkingDirectory_MissingDirectory_ThrowsArgumentException()
    {
        InputValidation.ValidateWorkingDirectory(Path.GetTempPath());

        Assert.Throws<ArgumentException>(
            () => InputValidation.ValidateWorkingDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [TestMethod]
    public void ValidateOutputLineCount_MustBePositive()
    {
        InputValidation.ValidateOutputLineCount(1);

        Assert.Throws<ArgumentException>(() => InputValidation.ValidateOutputLineCount(0));
        Assert.Throws<ArgumentException>(() => InputValidation.ValidateOutputLineCount(-1));
    }
}
