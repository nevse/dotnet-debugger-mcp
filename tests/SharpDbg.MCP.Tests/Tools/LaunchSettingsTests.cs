using SharpDbg.MCP.Tools;

namespace SharpDbg.MCP.Tests.Tools;

[TestClass]
public sealed class LaunchSettingsTests
{
    private string _project = null!;

    [TestInitialize]
    public void CreateProject()
    {
        _project = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"launch-settings-{Guid.NewGuid():N}")).FullName;
        Directory.CreateDirectory(Path.Combine(_project, "Properties"));
        File.WriteAllText(Path.Combine(_project, "App.csproj"), "<Project />");
    }

    [TestCleanup]
    public void DeleteProject() => Directory.Delete(_project, recursive: true);

    private void WriteSettings(string json) =>
        File.WriteAllText(Path.Combine(_project, "Properties", "launchSettings.json"), json);

    [TestMethod]
    public void Read_NamedProfile_TurnsItIntoLaunchParameters()
    {
        WriteSettings("""
            {
              // comments and trailing commas are what editors write
              "profiles": {
                "http": {
                  "commandName": "Project",
                  "commandLineArgs": "--port 5000 \"a b\"",
                  "workingDirectory": "data",
                  "applicationUrl": "http://localhost:5000",
                  "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" },
                },
              }
            }
            """);

        var profile = LaunchSettings.Read(Path.Combine(_project, "App.csproj"), "http")!;

        Assert.AreEqual("http", profile.Name);
        CollectionAssert.AreEqual(new[] { "--port", "5000", "a b" }, profile.Arguments!.ToArray());
        Assert.AreEqual(Path.Combine(_project, "data"), profile.WorkingDirectory);
        Assert.AreEqual("Development", profile.Environment["ASPNETCORE_ENVIRONMENT"]);
        Assert.AreEqual("http://localhost:5000", profile.Environment["ASPNETCORE_URLS"]);
    }

    [TestMethod]
    public void Read_ApplicationUrlAndProfileVariable_ProfileVariableWins()
    {
        WriteSettings("""
            { "profiles": { "p": {
                "commandName": "Project",
                "applicationUrl": "http://localhost:5000",
                "environmentVariables": { "ASPNETCORE_URLS": "http://localhost:6000" } } } }
            """);

        var profile = LaunchSettings.Read(_project, "p")!;

        Assert.AreEqual("http://localhost:6000", profile.Environment["ASPNETCORE_URLS"]);
        Assert.IsNull(profile.Arguments);
        Assert.IsNull(profile.WorkingDirectory);
    }

    [TestMethod]
    public void Read_WithoutAName_PrefersHttpsThenTheFirstProjectProfile()
    {
        WriteSettings("""
            { "profiles": {
                "IIS Express": { "commandName": "IISExpress" },
                "http": { "commandName": "Project" },
                "https": { "commandName": "Project" } } }
            """);

        Assert.AreEqual("https", LaunchSettings.Read(_project, null)!.Name);

        WriteSettings("""
            { "profiles": {
                "IIS Express": { "commandName": "IISExpress" },
                "http": { "commandName": "Project" } } }
            """);

        Assert.AreEqual("http", LaunchSettings.Read(_project, null)!.Name);
    }

    [TestMethod]
    public void Read_UnknownName_FailsAndListsTheProfiles()
    {
        WriteSettings("""{ "profiles": { "http": { "commandName": "Project" } } }""");

        var ex = Assert.ThrowsExactly<ArgumentException>(() => LaunchSettings.Read(_project, "htps"));

        StringAssert.Contains(ex.Message, "htps");
        StringAssert.Contains(ex.Message, "http");
    }

    /// <summary>
    /// An Executable profile names its own program, which would replace the one launch_program checked
    /// </summary>
    [TestMethod]
    public void Read_NonProjectProfile_IsRefused()
    {
        WriteSettings("""
            { "profiles": { "tool": { "commandName": "Executable", "executablePath": "/bin/sh" } } }
            """);

        var ex = Assert.ThrowsExactly<ArgumentException>(() => LaunchSettings.Read(_project, "tool"));

        StringAssert.Contains(ex.Message, "Executable");
        StringAssert.Contains(ex.Message, "Its profiles: tool");
    }

    /// <summary>
    /// A project without the file has no profile to use, which is only a mistake when one was named
    /// </summary>
    [TestMethod]
    public void Read_NoSettingsFile_IsAnErrorOnlyWhenAProfileWasNamed()
    {
        Assert.IsNull(LaunchSettings.Read(_project, null));

        var missing = Assert.ThrowsExactly<ArgumentException>(() => LaunchSettings.Read(_project, "http"));
        StringAssert.Contains(missing.Message, "launchSettings.json");
    }

    [TestMethod]
    public void Read_NotAProject_SaysWhichPath()
    {

        var dll = Path.Combine(_project, "App.dll");
        File.WriteAllText(dll, string.Empty);

        var notProject = Assert.ThrowsExactly<ArgumentException>(() => LaunchSettings.Read(dll, null));
        StringAssert.Contains(notProject.Message, "is not a project");
    }

    [TestMethod]
    public void Apply_CallerArgumentsAndDirectoryReplace_CallerVariablesGoOnTop()
    {
        var profile = new LaunchProfile(
            "p",
            ["--from-profile"],
            Path.GetFullPath("profile-cwd", _project),
            new Dictionary<string, string> { ["A"] = "profile", ["B"] = "profile" });

        var unchanged = profile.Apply(null, null, null);
        CollectionAssert.AreEqual(new[] { "--from-profile" }, unchanged.Arguments!.ToArray());
        Assert.AreEqual(profile.WorkingDirectory, unchanged.WorkingDirectory);
        Assert.AreEqual("profile", unchanged.Environment["A"]);

        var callerCwd = Path.GetFullPath("caller-cwd", _project);
        var overridden = profile.Apply(
            ["--from-caller"], callerCwd, new Dictionary<string, string> { ["B"] = "caller", ["C"] = "caller" });

        CollectionAssert.AreEqual(new[] { "--from-caller" }, overridden.Arguments!.ToArray());
        Assert.AreEqual(callerCwd, overridden.WorkingDirectory);
        Assert.AreEqual("profile", overridden.Environment["A"]);
        Assert.AreEqual("caller", overridden.Environment["B"]);
        Assert.AreEqual("caller", overridden.Environment["C"]);
    }

    /// <summary>
    /// Values are passed literally, as the debugger passes them
    /// </summary>
    [TestMethod]
    public void Read_PropertyAndVariableReferences_AreNotExpanded()
    {
        WriteSettings("""
            { "profiles": { "p": {
                "commandName": "Project",
                "environmentVariables": { "X": "$(ProjectDir)%HOME%" } } } }
            """);

        Assert.AreEqual("$(ProjectDir)%HOME%", LaunchSettings.Read(_project, "p")!.Environment["X"]);
    }

    [TestMethod]
    public void SplitCommandLine_FollowsTheWindowsQuotingRules()
    {
        CollectionAssert.AreEqual(
            new[] { "a", "b c", "", "d\"e", @"f\\g", @"h\", "i j" },
            LaunchSettings.SplitCommandLine(@"  a ""b c"" """" d\""e f\\g ""h\\"" i"" ""j ").ToArray());
    }
}
