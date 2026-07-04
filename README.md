# WikiExporter

`WikiExporter` is a .NET CLI tool that parses Allumeria source files and exports wiki-friendly JSON datasets.

## Exported datasets

- `items.json`
- `recipes.json`
- `blocks.json`
- `entities.json`
- `types.json`
- `summary.json`

## Usage

From the repository root:

```powershell
dotnet run --project .\WikiExporter
```

Arguments:

- `--source` or `-s`: path to the Allumeria source root folder. Defaults to `Allumeria` relative to the repository root.
- `--output` or `-o`: output directory for generated JSON files. Defaults to `wiki-export` in the current working directory.

## Notes

- This exporter uses source-text parsing with resilient regex and statement splitting.
- It is designed for the decompiled source layout currently in this repository.
- If source conventions change significantly, parser patterns may need updates.
