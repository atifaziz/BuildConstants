#region Copyright (c) 2026 Atif Aziz. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#endregion

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace IntegrationTests;

/// <summary>
/// Result of running <c>dotnet build</c> on a scenario project.
/// </summary>
sealed class ScenarioResult(int exitCode, string output, string scenarioDir)
{
    public int ExitCode { get; } = exitCode;
    public string Output { get; } = output;
    public string ScenarioDir { get; } = scenarioDir;

    /// <summary>
    /// Returns <c>true</c> if the build succeeded (exit code 0).
    /// </summary>
    public bool Succeeded => ExitCode == 0;

    /// <summary>
    /// Reads the generated <c>.g.cs</c> file from the scenario's <c>obj</c> directory.
    /// </summary>
    public string ReadGeneratedFile(string typeName = "BuildConstants")
    {
        var path = Path.Combine(ScenarioDir, "obj", ScenarioRunner.Configuration, "net10.0", $"{typeName}.g.cs");
        return File.ReadAllText(path);
    }
}

/// <summary>
/// Generates and builds scenario projects on the fly.
/// </summary>
static class ScenarioRunner
{
    static readonly string RepoRoot;
    static readonly string ScenariosDir;
    static readonly string TaskAssemblyPath;

#if DEBUG
    public const string Configuration = "Debug";
#else
    public const string Configuration = "Release";
#endif

    static ScenarioRunner()
    {
        // Resolve paths relative to the test assembly location.
        // The test assembly is at: tests/Integration/bin/<config>/net10.0/
        // Repo root is five levels up.
        var assemblyDir = AppContext.BaseDirectory;
        RepoRoot = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", "..", ".."));
        ScenariosDir = Path.Combine(RepoRoot, "tests", "Integration", "scenarios");

        // The task output is at: src/bin/<config>/netstandard2.0/BuildConstants.dll
        TaskAssemblyPath = Path.Combine(RepoRoot, "src", "bin", Configuration, "netstandard2.0", "BuildConstants.dll");
    }

    /// <summary>
    /// Wipe and recreate the scenarios directory. Called once per test run.
    /// </summary>
    public static void CleanScenariosDir()
    {
        if (Directory.Exists(ScenariosDir))
            Directory.Delete(ScenariosDir, recursive: true);
        _ = Directory.CreateDirectory(ScenariosDir);
    }

    /// <summary>
    /// Builds a scenario project on the fly.
    /// </summary>
    /// <param name="projectContent">The full content of the <c>.csproj</c> file (without the
    /// BuildConstants imports — those are injected automatically unless
    /// <paramref name="rawProject"/> is <c>true</c>).</param>
    /// <param name="files">Optional additional files to write (relative path → content).</param>
    /// <param name="rawProject">If <c>true</c>, writes <paramref name="projectContent"/> as-is
    /// without injecting the BuildConstants imports.</param>
    /// <param name="projectExtension">File extension for the project file (default <c>.csproj</c>).</param>
    /// <param name="testName">Scenario name (auto-populated from the calling method).</param>
    public static async Task<ScenarioResult> BuildAsync(
        string projectContent,
        Dictionary<string, string>? files = null,
        bool rawProject = false,
        string projectExtension = ".csproj",
        [CallerMemberName] string testName = "")
    {
        var scenarioDir = Path.Combine(ScenariosDir, testName);
        _ = Directory.CreateDirectory(scenarioDir);

        // Write the project file.
        var projectFileName = testName + projectExtension;
        var projectPath = Path.Combine(scenarioDir, projectFileName);

        if (!rawProject)
        {
            projectContent = InjectBuildConstantsImports(projectContent);
        }

        await File.WriteAllTextAsync(projectPath, projectContent);

        // Write additional files.
        if (files != null)
        {
            foreach (var (relativePath, content) in files)
            {
                var filePath = Path.Combine(scenarioDir, relativePath);
                var dir = Path.GetDirectoryName(filePath)!;
                _ = Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(filePath, content);
            }
        }

        // Write a Directory.Build.props that disables the repo-level one to avoid
        // it interfering with scenario builds.
        await File.WriteAllTextAsync(
            Path.Combine(scenarioDir, "Directory.Build.props"),
            "<Project></Project>\n");

        // Write a Directory.Build.targets that does nothing.
        await File.WriteAllTextAsync(
            Path.Combine(scenarioDir, "Directory.Build.targets"),
            "<Project></Project>\n");

        // Run dotnet build.
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c {Configuration} /nr:false --nologo -v:normal",
            WorkingDirectory = scenarioDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var combinedOutput = stdout + stderr;

        return new ScenarioResult(process.ExitCode, combinedOutput, scenarioDir);
    }

    /// <summary>
    /// Injects the BuildConstants <c>.props</c> and <c>.targets</c> imports into a <c>.csproj</c>.
    /// Also overrides <c>_BuildConstantsTaskAssembly</c> to point to the locally-built DLL.
    /// </summary>
    static string InjectBuildConstantsImports(string projectContent)
    {
        // We need to resolve the path to the build/ directory relative to the scenario,
        // but using absolute paths is safer for generated projects.
        var propsPath = Path.Combine(RepoRoot, "src", "build", "BuildConstants.props");
        var targetsPath = Path.Combine(RepoRoot, "src", "build", "BuildConstants.targets");

        var imports = $"""
              <Import Project="{propsPath}" />
              <PropertyGroup>
                <_BuildConstantsTaskAssembly>{TaskAssemblyPath}</_BuildConstantsTaskAssembly>
              </PropertyGroup>

            """;

        var targetsImport = $"""
              <Import Project="{targetsPath}" />

            """;

        // Insert the props import right after <Project ...> opening tag.
        var idx = projectContent.IndexOf('>') + 1;
        projectContent = projectContent.Insert(idx, "\n" + imports);

        // Insert the targets import right before </Project>.
        var endIdx = projectContent.LastIndexOf("</Project>", StringComparison.Ordinal);
        projectContent = projectContent.Insert(endIdx, targetsImport);

        return projectContent;
    }
}
