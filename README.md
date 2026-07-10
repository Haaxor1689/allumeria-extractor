# Allumeria Exporter

Allumeria Exporter is a .NET CLI tool that parses decompiled Allumeria source code and game assets, then writes structured JSON and WEBP assets for downstream use.

The exported data and assets are used by the Next.js Allumeria DB site: [Haaxor1689/allumeria-db](https://github.com/Haaxor1689/allumeria-db).

You can find the game on [Steam](https://store.steampowered.com/app/3516590/Allumeria/) or check out [the official website](https://allumeria.com/) for more links.

## Prerequisites

- .NET 8 SDK
- Access to the Allumeria game assets directory
- Decompiled Allumeria source in this repository under `Allumeria/`

## Prepare source before export

Before running the exporter, decompile and normalize the source tree.

1. Open the original `Allumeria.dll` assembly in dotPeek.
2. Export/decompile into this repository's `Allumeria/` folder.
3. From the repository root, run:

```powershell
.\Fix-AllumeriaSource.ps1
```

If these steps are skipped, parsers may fail or produce incomplete output.

## Run the exporter

Run from the repository root:

```powershell
dotnet run --project Exporter
```

Do not run the full solution build. Use the exporter project command above.

## CLI arguments

- `--source`, `-s`
	- Path to the Allumeria source root folder.
	- Default: `<current working directory>/Allumeria`
- `--output`, `-o`
	- Output directory for generated files.
	- Default: `<current working directory>/export`
- `--assets`, `-a`
	- Path to Allumeria assets root.
	- Default: `C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo\res`

Example:

```powershell
dotnet run --project Exporter -- --source .\Allumeria --output .\export --assets "C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo\res"
```

## Outputs

The exporter writes files under the selected output directory.

Data files (in `output/data/`):

- `items.json`
- `recipes.json`
- `blocks.json`
- `creatures.json`
- `loot.json`
- `spawn.json`
- `effects.json`
- `block_models.json`
- `translations.json`
- `summary.json`

Texture assets:

- `output/assets/items/` (converted item textures, WEBP)
- `output/assets/blocks/` (converted block textures, WEBP)
- `output/assets/ui/` (UI atlas slices, WEBP)