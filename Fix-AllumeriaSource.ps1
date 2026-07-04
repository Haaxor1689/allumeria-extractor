<#
.SYNOPSIS
    Fixes dotPeek decompiler artifacts in Allumeria source files so the project compiles cleanly.

.DESCRIPTION
    dotPeek emits several constructs that are not valid C# source:
      1. Auto-property backing fields as \u003CName\u003Ek__BackingField
         -> replaced with the property name directly
      2. Cached-delegate null-coalescing patterns using \u003C\u003EO.\u003CN\u003E__Method
         -> simplified to just "new Handler(Method)"
      3. Compiler-generated <PrivateImplementationDetails> inline-array helpers
         -> type renamed to _PrivateImplementationDetails_ (stub provided in Stubs\)
      4. All other \u003CName\u003E identifier escapes
         -> replaced with _Name_
      5. "// ISSUE: reference to a compiler-generated" comment noise
         -> removed

    After running this script, execute:
        dotnet format .\Allumeria.Source.csproj
    to normalize indentation (or use CSharpier if installed).

.PARAMETER SourcePath
    Root folder to scan. Defaults to .\Allumeria relative to the script location.

.PARAMETER DryRun
    Show which files would be changed without writing anything.

.EXAMPLE
    .\Fix-AllumeriaSource.ps1
    .\Fix-AllumeriaSource.ps1 -DryRun
#>
param(
    [string]$SourcePath = "$PSScriptRoot\Allumeria",
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $SourcePath)) {
    Write-Error "Source path not found: $SourcePath"
    exit 1
}

$files = Get-ChildItem -Path $SourcePath -Recurse -Filter '*.cs'
$fixedCount = 0
$totalCount = $files.Count

Write-Host "Scanning $totalCount .cs files in $SourcePath ..."
if ($DryRun) { Write-Host '(DRY RUN - no files will be modified)' -ForegroundColor Yellow }

foreach ($file in $files) {
    $path    = $file.FullName
    $content = [System.IO.File]::ReadAllText($path)
    $before  = $content

    # ------------------------------------------------------------------
    # 0. Explicit base constructor call emitted as base.\u002Ector(args)
    #    base.\u002Ector(...)  ->  base(...)
    #    (This must run before the generic \u003C..\u003E pass.)
    # ------------------------------------------------------------------
    $content = [System.Text.RegularExpressions.Regex]::Replace(
        $content,
        '(?<=\s)base\.\\u002Ector\(',
        'base('
    )

    # ------------------------------------------------------------------
    # 1. Auto-property backing fields
    #    this.\u003CSomeName\u003Ek__BackingField  ->  this.SomeName
    # ------------------------------------------------------------------
    $content = [System.Text.RegularExpressions.Regex]::Replace(
        $content,
        '\\u003C(\w+)\\u003Ek__BackingField',
        '$1'
    )

    # ------------------------------------------------------------------
    # 2. Cached-delegate null-coalescing pattern
    #    X.\u003C\u003EO.\u003CN\u003E__Method ?? (X.\u003C\u003EO.\u003CN\u003E__Method = new Handler(X.Method))
    #    ->  new Handler(X.Method)
    # ------------------------------------------------------------------
    $content = [System.Text.RegularExpressions.Regex]::Replace(
        $content,
        '[\w.]+\\u003C\\u003EO\.\\u003C\d+\\u003E__\w+[ \t]*\?\?[ \t]*\([\w.]+\\u003C\\u003EO\.\\u003C\d+\\u003E__\w+[ \t]*=[ \t]*(new \w+\([^)]+\))\)',
        '$1'
    )

    # ------------------------------------------------------------------
    # 3. Remove any leftover \u003C\u003EO delegate-cache field accesses
    #    that weren't part of a ?? assignment (edge cases)
    #    Replace the whole statement line with a comment.
    # ------------------------------------------------------------------
    $content = [System.Text.RegularExpressions.Regex]::Replace(
        $content,
        '^[ \t]*.+\\u003C\\u003EO\..+;\r?$',
        { param($m) $m.Value -replace '([ \t]*).*', '$1// [decompiler: cached-delegate field removed]' },
        [System.Text.RegularExpressions.RegexOptions]::Multiline
    )

    # ------------------------------------------------------------------
    # 4. All remaining \u003CName\u003E  ->  _Name_
    #    This renames <PrivateImplementationDetails> -> _PrivateImplementationDetails_
    #    and any other compiler-generated type/member names.
    # ------------------------------------------------------------------
    $content = [System.Text.RegularExpressions.Regex]::Replace(
        $content,
        '\\u003C([^\\]*)\\u003E',
        '_$1_'
    )

    # ------------------------------------------------------------------
    # 5. Remove "// ISSUE: reference to a compiler-generated ..." noise lines
    # ------------------------------------------------------------------
    $content = [System.Text.RegularExpressions.Regex]::Replace(
        $content,
        '[ \t]*// ISSUE: reference to a compiler-generated (?:field|method)\r?\n',
        '',
        [System.Text.RegularExpressions.RegexOptions]::None
    )

    if ($content -ne $before) {
        $fixedCount++
        if ($DryRun) {
            Write-Host "  Would fix: $($file.FullName.Replace($SourcePath, '').TrimStart('\'))"
        } else {
            # Preserve the original encoding (UTF-8 without BOM, or with BOM)
            $encoding = if ([System.IO.File]::ReadAllBytes($path)[0] -eq 0xEF) {
                [System.Text.UTF8Encoding]::new($true)   # with BOM
            } else {
                [System.Text.UTF8Encoding]::new($false)  # without BOM
            }
            [System.IO.File]::WriteAllText($path, $content, $encoding)
            Write-Host "  Fixed: $($file.FullName.Replace($SourcePath, '').TrimStart('\'))"
        }
    }
}

Write-Host ""
Write-Host "Done. $fixedCount / $totalCount files had decompiler artifacts fixed." -ForegroundColor Green

# ------------------------------------------------------------------
# Cleanup: remove the legacy Allumeria.csproj that dotPeek generated
# inside the Allumeria folder — it conflicts with the root project.
# ------------------------------------------------------------------
$legacyCsproj = Join-Path $SourcePath 'Allumeria.csproj'
if (Test-Path $legacyCsproj) {
    if ($DryRun) {
        Write-Host "  Would delete: $legacyCsproj" -ForegroundColor Yellow
    } else {
        Remove-Item $legacyCsproj -Force
        Write-Host "Deleted legacy project file: $legacyCsproj" -ForegroundColor Yellow
    }
}

if (-not $DryRun -and $fixedCount -gt 0) {
    Write-Host ""
    Write-Host "Next step: run 'dotnet format .\Allumeria.Source.csproj' to normalize indentation." -ForegroundColor Cyan
}
