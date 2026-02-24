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

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace IntegrationTests;

[Collection(ScenarioCollection.Name)]
public class ScenarioTests
{
    const string MinimalCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace>TestApp</RootNamespace>
          </PropertyGroup>
        </Project>
        """;

    static string MinimalCsprojWith(string innerXml) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace>TestApp</RootNamespace>
          </PropertyGroup>
          {innerXml}
        </Project>
        """;

    static string MinimalProgram(string body = "") => $$"""
        namespace TestApp;

        static class Program
        {
            static void Main()
            {
                {{body}}
            }
        }
        """;

    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DefaultConstants_AreGenerated()
    {
        var result = await ScenarioRunner.BuildAsync(
            MinimalCsproj,
            files: new() { ["Program.cs"] = MinimalProgram("System.Console.WriteLine(BuildConstants.AssemblyName);") });

        Assert.True(result.Succeeded, result.Output);
        var generated = result.ReadGeneratedFile();

        Assert.Contains("namespace TestApp;", generated);
        Assert.Contains("partial class BuildConstants", generated);

        // Default constants with non-empty values should be present.
        // Copyright and Description are typically empty in a bare project and
        // will be silently skipped.
        string[] expectedNames =
        [
            "AssemblyName", "AssemblyVersion", "FileVersion", "InformationalVersion",
            "Version", "Product", "Company", "Configuration"
        ];

        foreach (var name in expectedNames)
        {
            Assert.Contains($"public const string {name} = @\"", generated);
        }
    }

    [Fact]
    public async Task CustomStringConstant()
    {
        var csproj = MinimalCsprojWith("""
            <PropertyGroup>
              <EnableDefaultConstantItems>false</EnableDefaultConstantItems>
            </PropertyGroup>
            <ItemGroup>
              <Constant Include="AppName" Value="MyApp" />
            </ItemGroup>
            """);

        var result = await ScenarioRunner.BuildAsync(csproj,
            files: new() { ["Program.cs"] = MinimalProgram() });

        Assert.True(result.Succeeded, result.Output);
        var generated = result.ReadGeneratedFile();

        Assert.Contains("""public const string AppName = @"MyApp";""", generated);
    }

    [Fact]
    public async Task CustomBoolConstant()
    {
        var csproj = MinimalCsprojWith("""
            <PropertyGroup>
              <EnableDefaultConstantItems>false</EnableDefaultConstantItems>
            </PropertyGroup>
            <ItemGroup>
              <Constant Include="IsDebug" Value="true" Type="bool" />
            </ItemGroup>
            """);

        var result = await ScenarioRunner.BuildAsync(csproj,
            files: new() { ["Program.cs"] = MinimalProgram() });

        Assert.True(result.Succeeded, result.Output);
        var generated = result.ReadGeneratedFile();

        Assert.Contains("public const bool IsDebug = true;", generated);
    }

    [Fact]
    public async Task DisableDefaults()
    {
        var csproj = MinimalCsprojWith("""
            <PropertyGroup>
              <EnableDefaultConstantItems>false</EnableDefaultConstantItems>
            </PropertyGroup>
            <ItemGroup>
              <Constant Include="OnlyThis" Value="yes" />
            </ItemGroup>
            """);

        var result = await ScenarioRunner.BuildAsync(csproj,
            files: new() { ["Program.cs"] = MinimalProgram() });

        Assert.True(result.Succeeded, result.Output);
        var generated = result.ReadGeneratedFile();

        Assert.Contains("""public const string OnlyThis = @"yes";""", generated);
        Assert.DoesNotContain("AssemblyName", generated);
    }

    [Fact]
    public async Task RemoveConstant()
    {
        var csproj = MinimalCsprojWith("""
            <Target Name="_ExcludeCopyright" AfterTargets="PopulateConstants">
              <ItemGroup>
                <Constant Remove="Copyright" />
              </ItemGroup>
            </Target>
            """);

        var result = await ScenarioRunner.BuildAsync(csproj,
            files: new() { ["Program.cs"] = MinimalProgram() });

        Assert.True(result.Succeeded, result.Output);
        var generated = result.ReadGeneratedFile();

        Assert.Contains("AssemblyName", generated);
        Assert.DoesNotContain("Copyright", generated);
    }

    [Fact]
    public async Task CustomTypeName()
    {
        var csproj = MinimalCsprojWith("""
            <PropertyGroup>
              <ConstantsTypeName>AppInfo</ConstantsTypeName>
            </PropertyGroup>
            """);

        var result = await ScenarioRunner.BuildAsync(csproj,
            files: new() { ["Program.cs"] = MinimalProgram() });

        Assert.True(result.Succeeded, result.Output);
        var generated = result.ReadGeneratedFile("AppInfo");

        Assert.Contains("partial class AppInfo", generated);
    }

    [Fact]
    public async Task DocComments()
    {
        var csproj = MinimalCsprojWith("""
            <PropertyGroup>
              <EnableDefaultConstantItems>false</EnableDefaultConstantItems>
            </PropertyGroup>
            <ItemGroup>
              <Constant Include="AppName" Value="Test" Summary="Uses &lt;angle brackets&gt; &amp; more" />
            </ItemGroup>
            """);

        var result = await ScenarioRunner.BuildAsync(csproj,
            files: new() { ["Program.cs"] = MinimalProgram() });

        Assert.True(result.Succeeded, result.Output);
        var generated = result.ReadGeneratedFile();
        var summary =
            XElement.Parse(
                string.Join(null,
                            from line in Regex.Split(generated, @"\r?\n")
                            select line.Trim() into line
                            where line.StartsWith("/// ", StringComparison.Ordinal)
                            select line[4..]));

        Assert.Empty(summary.Name.NamespaceName);
        Assert.Equal("summary", summary.Name.LocalName);
        Assert.Equal("Uses <angle brackets> & more", summary.Value);
    }

    [Fact]
    public async Task InvalidName_ProducesError()
    {
        var csproj = MinimalCsprojWith("""
            <PropertyGroup>
              <EnableDefaultConstantItems>false</EnableDefaultConstantItems>
            </PropertyGroup>
            <ItemGroup>
              <Constant Include="123bad" Value="x" />
            </ItemGroup>
            """);

        var result = await ScenarioRunner.BuildAsync(csproj,
            files: new() { ["Program.cs"] = MinimalProgram() });

        Assert.False(result.Succeeded);
        Assert.Contains("123bad", result.Output);
        Assert.Contains("error", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateConstants_ProducesWarning()
    {
        var csproj = MinimalCsprojWith("""
            <PropertyGroup>
              <EnableDefaultConstantItems>false</EnableDefaultConstantItems>
            </PropertyGroup>
            <ItemGroup>
              <Constant Include="Foo" Value="first" />
              <Constant Include="Foo" Value="second" />
            </ItemGroup>
            """);

        var result = await ScenarioRunner.BuildAsync(csproj,
            files: new() { ["Program.cs"] = MinimalProgram() });

        Assert.True(result.Succeeded, result.Output);
        Assert.Contains("Duplicate", result.Output, StringComparison.OrdinalIgnoreCase);

        var generated = result.ReadGeneratedFile();
        Assert.Contains("""@"first""", generated);
        Assert.DoesNotContain("""@"second""", generated);
    }

    [Fact]
    public async Task EmptyValue_SilentlySkipped()
    {
        var csproj = MinimalCsprojWith("""
            <PropertyGroup>
              <EnableDefaultConstantItems>false</EnableDefaultConstantItems>
            </PropertyGroup>
            <ItemGroup>
              <Constant Include="Empty" Value="" />
              <Constant Include="Present" Value="here" />
            </ItemGroup>
            """);

        var result = await ScenarioRunner.BuildAsync(csproj,
            files: new() { ["Program.cs"] = MinimalProgram() });

        Assert.True(result.Succeeded, result.Output);
        var generated = result.ReadGeneratedFile();

        Assert.DoesNotContain("Empty", generated);
        Assert.Contains("Present", generated);
    }

    [Fact]
    public async Task NonCSharpProject_ProducesError()
    {
        // F# project — the task should reject non-C# projects.
        // Defaults are injected via the .props import, so values are passed.
        var fsproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """;

        var result = await ScenarioRunner.BuildAsync(
            fsproj,
            rawProject: false,
            projectExtension: ".fsproj",
            files: new() { ["Program.fs"] = """module Program""" });

        Assert.False(result.Succeeded);
        Assert.Contains("C#", result.Output);
    }

    [Fact]
    public async Task EmptyNamespace_GeneratesGlobalClass()
    {
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
                <EnableDefaultConstantItems>false</EnableDefaultConstantItems>
              </PropertyGroup>
              <ItemGroup>
                <Constant Include="AppName" Value="NoNs" />
              </ItemGroup>
            </Project>
            """;

        var result = await ScenarioRunner.BuildAsync(csproj,
            files: new() { ["Program.cs"] = """
                static class Program
                {
                    static void Main()
                    {
                        System.Console.WriteLine(BuildConstants.AppName);
                    }
                }
                """ });

        Assert.True(result.Succeeded, result.Output);
        var generated = result.ReadGeneratedFile();

        Assert.DoesNotContain("namespace", generated);
        Assert.Contains("partial class BuildConstants", generated);
        Assert.Contains("""public const string AppName = @"NoNs";""", generated);
    }

    [Fact]
    public async Task VerbatimStringEscaping()
    {
        var csproj = MinimalCsprojWith("""
            <PropertyGroup>
              <EnableDefaultConstantItems>false</EnableDefaultConstantItems>
            </PropertyGroup>
            <ItemGroup>
              <Constant Include="Quoted" Value='He said &quot;hello&quot;' />
            </ItemGroup>
            """);

        var result = await ScenarioRunner.BuildAsync(csproj,
            files: new() { ["Program.cs"] = MinimalProgram() });

        Assert.True(result.Succeeded, result.Output);
        var generated = result.ReadGeneratedFile();

        // In a verbatim string, " is escaped as ""
        Assert.Contains("He said \"\"hello\"\"", generated);
    }
}
