# Build Constants

An MSBuild task that generates a C# partial class with compile-time constants
from your project file.

## Motivation

Starting with C# 10, [string constants can be initialised with string
interpolations][strconst] and so the build constants can be used directly as
interpolation expressions (instead of, e.g., having to read them dynamically at
run-time via assembly attributes).

[strconst]: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-10.0/constant_interpolated_strings

```csharp
// Before: dynamic attribute reading at run-time
var version = Assembly.GetExecutingAssembly()
                      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                      .InformationalVersion;

Console.WriteLine($"App v{version}");

// After: compile-time constant, zero overhead
Console.WriteLine($"App v{BuildConstants.InformationalVersion}");
```

## Quick Start

1. Add the NuGet package to your C# project:

   ```shell
   dotnet add package BuildConstants
   ```

2. Build your project:

   ```shell
   dotnet build
   ```

3. Use the generated constants in your code:

   ```csharp
   Console.WriteLine($"{BuildConstants.Product} v{BuildConstants.Version}");
   Console.WriteLine($"{BuildConstants.Copyright}");
   ```

   including in _constants_ themselves:

   ```csharp
   const string ProductTitle = $"{Product} v{Version}";
   ```

That's it. By default, many [common project properties](#default-constants) are
exposed as `public const string` fields in a partial class called
`BuildConstants`, living in the project's root namespace.

## Default Constants

When `EnableDefaultConstantItems` is `true` (the default), the following
constants are automatically defined and mapped to the corresponding MSBuild
properties:

| Constant               | MSBuild Property           |
|------------------------|----------------------------|
| `AssemblyName`         | `$(AssemblyName)`          |
| `AssemblyVersion`      | `$(AssemblyVersion)`       |
| `FileVersion`          | `$(FileVersion)`           |
| `InformationalVersion` | `$(InformationalVersion)`  |
| `Version`              | `$(Version)`               |
| `Product`              | `$(Product)`               |
| `Company`              | `$(Company)`               |
| `Copyright`            | `$(Copyright)`             |
| `Description`          | `$(Description)`           |
| `Configuration`        | `$(Configuration)`         |

Constants whose property value is empty at build-time are silently omitted.

## Custom Constants

Add your own constants via `<Constant>` items in your project file:

```xml
<ItemGroup>
  <Constant Include="AppName" Value="My Cool App" />
  <Constant Include="LaunchYear" Value="2026" />
</ItemGroup>
```

Generated output:

```csharp
public const string AppName = @"My Cool App";
public const string LaunchYear = @"2026";
```

## Typed Constants

By default every constant is a `string`. Add `Type` metadata to emit a
different C# type. The supported types are:

| Type     | Value format                          |
|----------|---------------------------------------|
| `string` | Any text (default; `Type` can be omitted) |
| `bool`   | `true` or `false`                     |
| `int`    | Any valid C# `int` literal (e.g., `42`, `-1`, `0xFF`) |

```xml
<ItemGroup>
  <Constant Include="IsDebug"    Value="true" Type="bool" />
  <Constant Include="MaxRetries" Value="5"    Type="int"  />
</ItemGroup>
```

Generated output:

```csharp
public const bool IsDebug = true;
public const int MaxRetries = 5;
```

Non-string values are emitted verbatim without validation, so they must be valid
C# literals for the chosen type otherwise the compiler will generate an error during compilation of the generated file.

## Doc Comments

Add a `Summary` metadata to include an XML doc comment on the constant:

```xml
<ItemGroup>
  <Constant Include="AppName" Value="My App"
            Summary="The display name of the application." />
</ItemGroup>
```

Generated output:

```csharp
/// <summary>The display name of the application.</summary>
public const string AppName = @"My App";
```

Special XML characters (`<`, `>`, `&`, `"`, `'`) in the summary text are
automatically encoded.

## Removing Constants

Remove any default constant by adding a target that runs after
`PopulateConstants`:

```xml
<Target Name="_ExcludeConstants" AfterTargets="PopulateConstants">
  <ItemGroup>
    <Constant Remove="Copyright" />
    <Constant Remove="Description" />
  </ItemGroup>
</Target>

```

## Custom Class Name

By default the generated class is called `BuildConstants`. Override it with the
`ConstantsTypeName` property:

```xml
<PropertyGroup>
  <ConstantsTypeName>AppInfo</ConstantsTypeName>
</PropertyGroup>
```

The generated file will contain `partial class AppInfo` instead.

## Disabling Defaults

Set `EnableDefaultConstantItems` to `false` to suppress all default constants
and define only your own:

```xml
<PropertyGroup>
  <EnableDefaultConstantItems>false</EnableDefaultConstantItems>
</PropertyGroup>

<ItemGroup>
  <Constant Include="AppName" Value="My App" />
</ItemGroup>
```

## Validation Rules

- **Constant names** must begin with an upper-case letter, followed by upper- or
  lower-case letters or digits. Invalid names produce a build error.
- **Empty values** are silently skipped (no constant is emitted).
- **Duplicate names** produce a build warning; only the first occurrence is
  kept.
- **Allowed types** are `string` (default), `bool`, and `int`. Any other type
  produces a build error.
- The task **only supports C# projects**. Using it in an F# or VB project
  produces a build error.
