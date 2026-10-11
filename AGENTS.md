# AGENTS.md

mRemoteNG is a Windows-only .NET 10 remote connection manager (RDP, VNC, SSH, Telnet, HTTP/S, raw sockets, PowerShell remoting, AnyDesk). Fork of mRemote.

## Build

- **Must use the Visual Studio 2026 MSBuild for anything in `mRemoteNG.sln`** — `dotnet build` fails on the app project (T4 text templating, COM `MSTSCLib` reference, WPF/WinForms desktop targets). Locate it with `vswhere` if the path differs:
  ```powershell
  & "$env:ProgramFiles(x86)\Microsoft Visual Studio\2026\Enterprise\MSBuild\Current\Bin\MSBuild.exe" mRemoteNG.sln /p:Configuration=Debug /p:Platform=x64
  ```
- **`Platform` is always required** (`x64` or `arm64`); there is no AnyCPU. Output paths embed it (`mRemoteNG\bin\x64\Release\`). ARM64 CI builds run on ARM64 runners (`windows-11-vs2026-arm`), so they can't be reproduced on x64 hosts.
- Self-contained release config name is `Release Self-Contained` (value must be quoted on MSBuild CLI), `RuntimeIdentifier=win-x64|win-arm64`, `SelfContained=true`, `PublishReadyToRun=true`.
- App targets `net10.0-windows10.0.26100.0`. `Nullable` is **disabled**; several CS warnings are suppressed in `mRemoteNG.csproj`.

## Two separate solutions

- `mRemoteNG.sln` = app + `ExternalConnectors` + `mRemoteNG.PluginContracts` + `ObjectListView.NetCore` + `mRemoteNGTests` + installer `CustomActions`.
- `mRemoteNG.Plugins.sln` = contracts + the 4 plugins (`PortScan`, `SshTransfer`, `AWS`, `MultiAddress`) + their test projects. The app **does not reference** plugin projects; they're loaded at runtime from the output `Plugins\` folder.
- Build plugins (plain .NET, no T4/COM, `dotnet` works):
  ```powershell
  dotnet build mRemoteNG.Plugins.sln -c Release -p:Platform=x64
  ```
- Do **not** add plugin projects to `mRemoteNG.sln`.

## Output layout (easy to break)

`mRemoteNG.csproj` has post-build/publish targets that relocate assemblies:
- Third-party NuGet DLLs → `Assemblies\` (kept in `runtimes\...` for native assets).
- `mRp.Contracts.dll` → `Assemblies\`, other `mRp.*.dll` → `Plugins\`.
- Satellite resource assemblies → `Languages\<culture>\`.

When you change csproj references/targets, keep these relocation targets working — the CI (`Build_mR-NB.yml`) hard-fails if this layout is wrong.

## Generated code — do not hand-edit

- `mRemoteNG\Properties\AssemblyInfo.cs` is generated from `AssemblyInfo.tt` (build number = minutes since 2019-09-02 / 1000; platform passed via `-P platformType`). With VS MSBuild it regenerates on build; CI also runs `dotnet-t4` with `-r:...\Microsoft.VisualStudio.Interop.dll`. The extract-build-number step in CI reads `AssemblyVersion` from this file.
- `Language\Language.Designer.cs` is generated from `Language.resx`; `Options*Page.Designer.cs` / `Settings.Designer.cs` come from `.settings` files. Edit the source (`resx`/`.settings`), not the `.Designer.cs`.

## Testing

- NUnit, mocking via NSubstitute. Main suite: `mRemoteNGTests\` (mirrors app folders, references `mRemoteNG.csproj`). Plugin suites live beside each plugin and are in `mRemoteNG.Plugins.sln`.
- `mRemoteNGSpecs\` is a separate SpecFlow-style (Gherkin `Features\`) project — `net9.0`, `WinExe`, x64 only, and it is **not** in either solution.
- Run tests with `dotnet test <csproj>` (needs `-p:Platform=x64` if a Platform default causes issues); the app-referencing suite requires the VS2026 toolchain/T4 just like a build.

## Conventions

- Style enforced by `mRemoteNG\.editorconfig` (it is `root = true`, scoped to the `mRemoteNG\` subtree): CRLF, UTF-8 **with BOM** for `.cs`, 4-space indent, Allman braces, no `this.` qualifier, `System` usings sorted first.
- **Localize every user-facing string**: add to `Language\Language.resx`, reference as `Language.<ResourceName>`. Naming: `PropertyName` for the property, `PropertyDescription<PropertyName>` for PropertyGrid tooltips, `<EnumName><Value>` for enum values.
- **Never break loading of old connection files.** The XML format is versioned; `Config\Serializers\ConnectionSerializers\Xml\XmlConnectionNodeSerializer28.cs` is the newest serializer and `XmlConnectionsDeserializer.cs` handles mixing versions. New properties need defaults so old files load.
- New connection properties follow a fixed checklist: enum (if needed) → `AbstractConnectionRecord.cs`/`ConnectionInfo.cs` with `[Category]`/`[Description]` → inheritance flag in `ConnectionInfoInheritance.cs` → XML + CSV serializers → `Language.resx` → tests → `mRemoteNGDocumentation\` if user-facing.
- RDP uses the `MSTSCLib` COM reference (mstscax.dll); SSH/Telnet run the bundled `PuTTYNG.exe`. Platform-specific code is avoided unless necessary.

## Release pipeline

- Nightly builds are published by `.github/workflows/Build_mR-NB.yml`, triggered by a push to `v1.78.2-dev` whose commit message contains `NB release`, or manual dispatch. Changelog entries go in the current `CHANGELOG.md` section under `### Fixed` / `### Added` / `### Updated` (entry format `- #<issue>: <description>`).
- Renovate keeps dependency versions in `Directory.Packages.props` (central package management) — add new packages there with a `PackageVersion`, reference them in csproj without a version.