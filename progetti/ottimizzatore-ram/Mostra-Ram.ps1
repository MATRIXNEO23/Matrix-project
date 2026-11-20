# Mostra una rilevazione dei processi e della RAM privata. Esempio : .\Mostra-Ram.ps1 | Format-Table
$ErrorActionPreference = 'Stop'

$before = @{}
Get-CimInstance Win32_Process -Property ProcessId, CreationDate | ForEach-Object {
    $before[[uint32]$_.ProcessId] = $_.CreationDate
}

$memory = @{}
Get-CimInstance Win32_PerfRawData_PerfProc_Process -Property IDProcess, WorkingSetPrivate | ForEach-Object {
    if ($_.IDProcess -gt 0 -and $null -ne $_.WorkingSetPrivate) {
        $memory[[uint32]$_.IDProcess] = [uint64]$_.WorkingSetPrivate
    }
}

$byId = @{}
$fileInfo = @{}
foreach ($process in Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, CreationDate, ExecutablePath, Name) {
    $id = [uint32]$process.ProcessId
    $path = $process.ExecutablePath
    $bytes = $null
    if ($process.CreationDate -and $before[$id] -eq $process.CreationDate -and $memory.ContainsKey($id)) {
        $bytes = $memory[$id]
    }

    if ($path -and -not $fileInfo.ContainsKey($path)) {
        $fileInfo[$path] = $null
        try {
            $fileInfo[$path] = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path)
        } catch [System.IO.IOException] {
            # Il file potrebbe essere scomparso dopo la rilevazione.
        } catch [System.UnauthorizedAccessException] {
            # Il percorso resta disponibile per identificare il processo.
        }
    }
    $info = if ($path) { $fileInfo[$path] }
    $byId[$id] = [pscustomobject]@{
        Id = $id
        ParentId = [uint32]$process.ParentProcessId
        Started = $process.CreationDate
        Name = $process.Name
        Path = $path
        Directory = if ($path) { [IO.Path]::GetDirectoryName($path) }
        Product = $info.ProductName
        Publisher = $info.CompanyName
        Bytes = $bytes
    }
}

# Legge le installazioni correnti per riconoscere anche programmi aggiunti in seguito.
$installations = @{}
$ambiguousRoots = @{}
$uninstallKeys = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
$unsafeRoots = @(
    $env:SystemDrive, $env:windir, $env:ProgramFiles, ${env:ProgramFiles(x86)}
    $env:ProgramData, $env:USERPROFILE, $env:LOCALAPPDATA, $env:APPDATA
    (Join-Path $env:LOCALAPPDATA 'Programs')
    (Join-Path $env:ProgramFiles 'Common Files')
    (Join-Path $env:ProgramFiles 'WindowsApps')
)
foreach ($entry in Get-ItemProperty -Path $uninstallKeys -ErrorAction SilentlyContinue) {
    if (-not $entry.DisplayName -or -not $entry.InstallLocation -or $entry.SystemComponent -eq 1) { continue }
    $location = [Environment]::ExpandEnvironmentVariables(([string]$entry.InstallLocation).Trim().Trim('"'))
    if ($location -notmatch '^[A-Za-z]:\\') { continue }
    try {
        $root = [IO.Path]::GetFullPath($location).TrimEnd('\')
    } catch [System.ArgumentException], [System.NotSupportedException], [System.IO.PathTooLongException] {
        continue
    }
    if ($root -in $unsafeRoots -or $root.Length -le 3) { continue }
    if ($installations.ContainsKey($root) -and $installations[$root] -ne $entry.DisplayName) {
        $ambiguousRoots[$root] = $true
    }
    $installations[$root] = [string]$entry.DisplayName
}

$installRoots = @($installations.Keys | Sort-Object Length -Descending)
$installationByPath = @{}
foreach ($member in $byId.Values) {
    if (-not $member.Path -or $installationByPath.ContainsKey($member.Path)) { continue }
    $installationByPath[$member.Path] = $null
    foreach ($root in $installRoots) {
        if ($member.Path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
            if (-not $ambiguousRoots.ContainsKey($root)) {
                $installationByPath[$member.Path] = $root
            }
            break
        }
    }
}

# I componenti Codex distribuiti fuori dal pacchetto possono essere privi di metadati.
$codexRoot = [Environment]::GetFolderPath('LocalApplicationData') + '\OpenAI\Codex\bin\'
$packagesRoot = [regex]::Escape((Join-Path $env:ProgramFiles 'WindowsApps'))
$codexApp = '^' + $packagesRoot + '\\OpenAI\.Codex_[^\\]+__2p2nqsd0c76g0\\app\\ChatGPT\.exe\z'
$codexHelpers = @('codex.exe', 'codex-code-mode-host.exe')

$byId.Values | Group-Object {
    $member = $_
    if (-not $member.Path) { return "pid:$($member.Id)" }
    $parent = $byId[$member.ParentId]
    while ($parent -and $parent.Started -and $member.Started -and
        $parent.Started -lt $member.Started -and $parent.Path) {
        $sameProduct = $member.Product -and $member.Publisher -and
            $parent.Product -eq $member.Product -and $parent.Publisher -eq $member.Publisher -and
            $parent.Directory -eq $member.Directory

        $codexHelper = $member.Name -in $codexHelpers -and
            $member.Path.StartsWith($codexRoot, [StringComparison]::OrdinalIgnoreCase)
        $codexParent = $parent.Path -match $codexApp -or
            ($parent.Name -eq 'codex.exe' -and $parent.Directory -eq $member.Directory)
        if (-not ($sameProduct -or ($codexHelper -and $codexParent))) { break }

        $member = $parent
        $parent = $byId[$member.ParentId]
    }
    $root = $installationByPath[$member.Path]
    if ($root) { return "install:$root" }
    $member.Path
} | ForEach-Object {
    $members = $_.Group
    $measured = @($members | Where-Object { $null -ne $_.Bytes })
    $reference = $members | Where-Object Path -EQ $_.Name | Select-Object -First 1
    $label = 'App da identificare'
    if ($reference) {
        $label = $reference.Name
        if ($reference.Product) { $label = $reference.Product }
    }
    $installation = $null
    if ($_.Name.StartsWith('install:', [StringComparison]::Ordinal)) {
        $installation = $_.Name.Substring('install:'.Length)
        $label = $installations[$installation]
    }
    $totalBytes = $null
    if ($measured.Count) {
        $totalBytes = [uint64]0
        foreach ($item in $measured) { $totalBytes += $item.Bytes }
    }
    [pscustomobject]@{
        Applicazione = $label
        Eseguibile = $reference.Path
        CartellaInstallazione = $installation
        Processi = @($members | Sort-Object Id | Select-Object Id, Name, Path, Bytes)
        RamPrivataByte = $totalBytes
        TotaleParziale = $measured.Count -lt $members.Count
    }
} | Sort-Object RamPrivataByte -Descending
