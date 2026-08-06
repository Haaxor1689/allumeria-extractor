# Allumeria Exporter

Allumeria Exporter is a .NET CLI tool that reads game data and assets from the Allumeria install, then writes structured JSON and WEBP assets for downstream use.

The exported data and assets are used by the Next.js Allumeria DB site: [Haaxor1689/allumeria-db](https://github.com/Haaxor1689/allumeria-db).

You can find the game on [Steam](https://store.steampowered.com/app/3516590/Allumeria/) or check out [the official website](https://allumeria.com/) for more links.

## Prerequisites

- .NET 8 SDK
- Allumeria game assets

## Run the exporter

Run from the repository root:

```powershell
dotnet run
```

## CLI arguments

- `--out-assets`, `-oa`
	- Output directory for exported WEBP assets.
	- Default: `<current working directory>/export/assets`
- `--out-data`, `-od`
	- Output directory for exported JSON data.
	- Default: `<current working directory>/export/data`

## Environment variable

- `ALLUMERIA_INSTALL_DIR`
	- Optional path to the Allumeria install directory.
	- If not set, the exporter falls back to the default Steam install directory:
	  `C:\Program Files (x86)\Steam\steamapps\common\Allumeria Demo`

Example:

```powershell
dotnet run -- --out-data C:\Projects\_Allumeria\allumeria-db\src\data --out-assets C:\Projects\_Allumeria\allumeria-db\public\assets
```

## Outputs

The exporter writes files to the selected `--out-data` and `--out-assets` directories.

Data files (in your `--out-data` directory):

- `block_materials.json`
- `block_models.json`
- `blocks.json`
- `catalogues.json`
- `effects.json`
- `entities.json`
- `item_tags.json`
- `items.json`
- `loot.json`
- `recipe_aliases.json`
- `recipes.json`
- `spawn.json`
- `structures.json`
- `summary.json`
- `translations.json`

Asset outputs (under your `--out-assets` directory):

- `blocks/` (converted block textures, WEBP)
- `effects/` (effect atlas slices, WEBP)
- `item_tags/` (item tag icon slices, WEBP)
- `items/` (converted item textures, WEBP)
- `models/*/` (copied model JSON organized by model id prefix)
- `textures/*/` (converted textures to WEBP)
- `ui/` (UI atlas slices, WEBP)