# Installation

## Prerequisites

- [.NET SDK 10.0 or later](https://dotnet.microsoft.com/download) (LTS)
- Git (to clone the repository)
- Windows, Linux, or macOS

## Install the CLI

From a clone of the repository:

```bash
./scripts/setup.sh        # restores and builds the solution
./scripts/install-cli.sh  # packs and installs the `websiter` tool globally
```

Windows (PowerShell):

```powershell
.\scripts\setup.ps1
.\scripts\install-cli.ps1
```

Verify:

```bash
websiter --version
websiter --help
```

## Alternative: run from source

```bash
dotnet run --project src/Websiter.CLI -- --help
```

## Uninstall

```bash
./scripts/uninstall-cli.sh
```
