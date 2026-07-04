# Exporter

`Exporter` is a .NET CLI tool that parses Allumeria source files and exports JSON datasets.

## Prepare source before export

Before running the exporter project, you must decompile and normalize the source tree.

1. Open the original `Allumeria.dll` assembly in dotPeek.
2. Export/decompile the project into this repository's `Allumeria` folder so source files exist under `./Allumeria`.
3. From the repository root, run the fix script:

```powershell
.\Fix-AllumeriaSource.ps1
```

The exporter expects this fixed decompiled layout. If you skip these steps, export parsing may fail or produce incomplete JSON.

## Exported datasets

- `items.json`
- `recipes.json`
- `blocks.json`
- `creatures.json`
- `loot.json`
- `spawn.json`
- `summary.json`

## Usage

From the repository root:

```powershell
dotnet run --project Exporter
```

Arguments:

- `--source` or `-s`: path to the Allumeria source root folder. Defaults to `Allumeria` relative to the repository root.
- `--output` or `-o`: output directory for generated JSON files. Defaults to `export` in the current working directory.
- `--assets` or `-a`: path to the Allumeria assets folder inside Steam. Defaults to `{path_to_steamapps_common}/Allumeria Demo/res`.

