# Release checks shared by Build-Release.ps1 and the tests (dot-sourced).

# Nothing shipped may name the developer: not in text, code comments, metadata or binaries. The names come from
# private-names.txt next to this script (kept private, so this script itself names nobody) plus this PC's Windows user.
# Text files: the names as whole words (exact case). Binaries (compressed data, where any 3 letters turn up by
# chance): only patterns too long to be random - names of 6+ letters, this PC's user folder - as plain and UTF-16 text,
# plus the exe's version fields.
function Assert-NoNames([string[]]$paths) {
    $listFile = Join-Path $PSScriptRoot 'private-names.txt'
    $names = @(if (Test-Path $listFile) { Get-Content $listFile | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') } })
    if ($env:USERNAME) { $names += $env:USERNAME }
    $textNames = '\b(' + (($names | Sort-Object -Unique | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\b'
    $long = @($names | Where-Object { $_.Length -ge 6 } | ForEach-Object { [regex]::Escape($_) })
    $binNames = '(?i)(' + (@($long) + [regex]::Escape($env:USERPROFILE) + ('\\Users\\' + [regex]::Escape($env:USERNAME) + '\\') -join '|') + ')'
    # Every text format the public repo / installer contains (source too: a short name in a .cs comment slipped through once).
    $textExt = '.lua', '.toc', '.ps1', '.md', '.txt', '.rtf', '.inf', '.json', '.xml', '.wxs', '', '.cs', '.csproj', '.wixproj', '.props',
        '.targets', '.html', '.htm', '.css', '.js', '.svg', '.yml', '.yaml', '.gitignore', '.gitattributes', '.config', '.manifest', '.resx', '.sln'
    $hits = foreach ($f in Get-ChildItem $paths -Recurse -File) {
        if ($textExt -contains $f.Extension.ToLowerInvariant()) {
            $m = [regex]::Match([IO.File]::ReadAllText($f.FullName), $textNames)
            if ($m.Success) { "$($f.Name): '$($m.Value)'" }
            continue
        }
        $bytes = [IO.File]::ReadAllBytes($f.FullName)
        foreach ($enc in [Text.Encoding]::ASCII, [Text.Encoding]::Unicode) {
            $m = [regex]::Match($enc.GetString($bytes), $binNames)
            if ($m.Success) { "$($f.Name): '$($m.Value)'"; break }
        }
        if ($f.Extension -eq '.exe') {
            $v = $f.VersionInfo
            foreach ($field in $v.LegalCopyright, $v.CompanyName, $v.ProductName, $v.FileDescription) {
                if ($field -and [regex]::IsMatch($field, $textNames)) { "$($f.Name) version info: '$field'" }
            }
        }
    }
    if ($hits) { throw "Personal names in release files: $($hits -join '; ')" }
}
