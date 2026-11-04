# Repository Guidelines

## Project Structure & Module Organization

`LaserGRBL/` contains the Windows Forms application. Core controller logic lives in `LaserGRBL/Core/`; machine communication adapters are under `ComWrapper/`; G-code and image conversion code is grouped in directories such as `SvgConverter/`, `RasterConverter/`, `CsPotrace/`, and `Generator/`. UI forms generally use matching `.cs`, `.Designer.cs`, and `.resx` files. Localized resources add a culture suffix, for example `MainForm.es.resx`. Static icons, images, and GRBL lookup tables live in `LaserGRBL/Icons/`, `LaserGRBL/Grafica/`, and `LaserGRBL/CSV/`.

`LaserGRBL.Tests/` contains xUnit tests, currently organized by feature. Installer metadata is in `setup.iss`; contributor and translation details are in `CONTRIBUTING.md` and `TRANSLATING.md`.

## Build, Test, and Development Commands

Run commands from a Visual Studio Developer PowerShell on Windows:

```powershell
msbuild LaserGRBL.sln /p:Configuration=Debug
msbuild LaserGRBL.sln /p:Configuration=Release
dotnet test LaserGRBL.Tests\LaserGRBL.Tests.csproj
dotnet test LaserGRBL.Tests\LaserGRBL.Tests.csproj --collect:"XPlat Code Coverage"
```

The application targets .NET Framework 4.0 and produces output under `LaserGRBL/bin/<Configuration>/`. The test project targets .NET Core 3.1 and is not included in `LaserGRBL.sln`, so run it explicitly. Launch `LaserGRBL/bin/Debug/LaserGRBL.exe` for local UI checks.

## Coding Style & Naming Conventions

Follow the surrounding C# style; this older codebase contains both tabs and four-space indentation, so avoid unrelated reformatting. Use PascalCase for types and public members, camelCase for locals and parameters, and preserve established folder namespaces. Keep WinForms designer-generated code in `.Designer.cs`; make behavioral changes in the corresponding `.cs` file. When changing visible text, update the neutral `.resx` and relevant localized resources without renaming existing resource keys.

## UI Design Guidelines

Before creating or modifying any UI component, always read and follow the root [`design.md`](design.md). Apply its palette, typography, spacing, component states, accessibility, and interaction rules to every UI change, including forms, dialogs, menus, toolbars, tables, and canvas controls. Reuse the existing shared theme and components to keep the interface consistent.

## Testing Guidelines

Tests use xUnit with `[Fact]` and `[Theory]`/`[InlineData]`. Name test classes after the subject and methods as `Method_Scenario_ExpectedResult`. Add focused regression tests near the affected feature. There is no enforced coverage threshold, but exercise boundary and failure cases. Hardware-dependent changes also require a manual smoke test without issuing unsafe machine commands.

## Commit & Pull Request Guidelines

Recent history uses short, imperative subjects such as `Fix exception...` and `Update setup.iss`; keep each commit narrowly scoped. Pull requests should explain the problem, approach, and verification performed, and link the relevant issue. Include screenshots for UI changes and call out affected firmware, devices, translations, or installer behavior.
