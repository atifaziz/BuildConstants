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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace BuildConstants;

/// <summary>
/// MSBuild task that generates a C# partial class containing public constants
/// from <c>&lt;Constant&gt;</c> items defined in the project file.
/// </summary>
public class GenerateBuildConstantsTask : Task
{
    static readonly Regex ValidNamePattern = new(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.Compiled);

    static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
    {
        "string",
        "bool",
    };

    /// <summary>
    /// The <c>&lt;Constant&gt;</c> items to generate. Each item's <c>Include</c> is the
    /// constant name, with <c>Value</c>, <c>Type</c>, and <c>Summary</c> metadata.
    /// </summary>
    [Required]
#pragma warning disable CA1819 // Properties should not return arrays (by-contract)
    public ITaskItem[] Constants { get; set; } = [];
#pragma warning restore CA1819 // Properties should not return arrays

    /// <summary>
    /// The namespace for the generated class (typically <c>$(RootNamespace)</c>).
    /// </summary>
    [Required]
    public string Namespace { get; set; } = "";

    /// <summary>
    /// The name of the generated class. Defaults to <c>BuildConstants</c>.
    /// </summary>
    [Required]
    public string TypeName { get; set; } = "BuildConstants";

    /// <summary>
    /// The full path of the generated <c>.cs</c> file.
    /// </summary>
    [Required]
    public string OutputPath { get; set; } = "";

    /// <summary>
    /// The project language (e.g. <c>C#</c>). An error is emitted if this is not <c>C#</c>.
    /// </summary>
    [Required]
    public string Language { get; set; } = "";

    /// <summary>
    /// Returns the full path of the generated file (same as <see cref="OutputPath"/>).
    /// </summary>
    [Output]
    public string GeneratedFilePath { get; set; } = "";

    public override bool Execute()
    {
        if (!string.Equals(Language, "C#", StringComparison.OrdinalIgnoreCase))
        {
            Log.LogError("BuildConstants only supports C# projects. The current project language is \"{0}\".", Language);
            return false;
        }

        var entries = new List<ConstantEntry>();
        var nameSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, value, type, summary) in
                 from item in Constants
                 select (item.ItemSpec,
                         Value: item.GetMetadata("Value") ?? "",
                         item.GetMetadata("Type") switch
                         {
                             null or "" => "string",
                             var t => t,
                         },
                         item.GetMetadata("Summary"))
                 into item
                 where !string.IsNullOrEmpty(item.Value)
                 select item)
        {
            if (!ValidNamePattern.IsMatch(name))
            {
                Log.LogError(
                    "Invalid constant name \"{0}\". Names must begin with an upper-case letter, "
                    + "followed by upper- or lower-case letters or digits (pattern: {1}).",
                    name,
                    ValidNamePattern.ToString());
                continue;
            }

            // Detect duplicates.
            if (!nameSet.Add(name))
            {
                Log.LogWarning("Duplicate constant \"{0}\". Only the first occurrence will be used.", name);
                continue;
            }

            if (!AllowedTypes.Contains(type))
            {
                Log.LogError(
                    "Unsupported type \"{0}\" for constant \"{1}\". Allowed types are: {2}.",
                    type,
                    name,
                    string.Join(", ", AllowedTypes));
                continue;
            }

            entries.Add(new ConstantEntry(name, value, type, summary));
        }

        if (Log.HasLoggedErrors)
            return false;

        var code = GenerateCode(Namespace, TypeName, entries);

        var directory = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(directory))
            _ = Directory.CreateDirectory(directory);

        File.WriteAllText(OutputPath, code, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        GeneratedFilePath = OutputPath;

        return true;
    }

    static string GenerateCode(string ns, string typeName, List<ConstantEntry> entries)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("// <auto-generated/>");
        _ = sb.AppendLine();

        if (!string.IsNullOrEmpty(ns))
            _ = sb.Append("namespace ").Append(ns).AppendLine(";").AppendLine();

        _ = sb.Append("partial class ").AppendLine(typeName);
        _ = sb.AppendLine("{");

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (!string.IsNullOrEmpty(entry.Summary))
            {
                _ = sb.Append("    /// <summary>").Append(XmlEncode(entry.Summary)).AppendLine("</summary>");
            }

            _ = sb.Append("    public const ").Append(entry.Type).Append(' ').Append(entry.Name).Append(" = ");

            _ = string.Equals(entry.Type, "string", StringComparison.Ordinal)
                ? sb.Append("@\"").Append(entry.Value.Replace("\"", "\"\"")).Append('"')
                : sb.Append(entry.Value); // bool and any future verbatim-value types.

            _ = sb.AppendLine(";");

            if (i < entries.Count - 1)
                _ = sb.AppendLine();
        }

        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    static string XmlEncode(string text) =>
        SecurityElement.Escape(text) ?? text;

    readonly record struct ConstantEntry(string Name, string Value, string Type, string Summary);
}
