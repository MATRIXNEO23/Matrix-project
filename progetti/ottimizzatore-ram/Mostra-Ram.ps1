# Mostra una rilevazione dei processi e della RAM privata. Esempio : .\Mostra-Ram.ps1 | Format-Table
# Per valutare le proposte: .\Mostra-Ram.ps1 -ConfermaAssociazioni
# Per mostrare i suggerimenti oltre l'80%: .\Mostra-Ram.ps1 -SogliaRamPercento 80
param(
    [switch]$ConfermaAssociazioni,
    [ValidateRange(1, 100)]
    [int]$SogliaRamPercento
)

$ErrorActionPreference = 'Stop'
$usaSoglia = $PSBoundParameters.ContainsKey('SogliaRamPercento')
$caricoRam = $null
if ($usaSoglia) {
    try {
        $os = Get-CimInstance Win32_OperatingSystem -Property TotalVisibleMemorySize, FreePhysicalMemory
        if ($os.TotalVisibleMemorySize -gt 0 -and $null -ne $os.FreePhysicalMemory -and
            $os.FreePhysicalMemory -le $os.TotalVisibleMemorySize) {
            $caricoRam = 100 * ([decimal]$os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / $os.TotalVisibleMemorySize
        }
    } catch [Microsoft.Management.Infrastructure.CimException] {
        # Mantiene disponibile la rilevazione per processo.
    }
    if ($null -eq $caricoRam) {
        Write-Warning 'Carico RAM di sistema non disponibile. Suggerimenti sospesi.'
    }
}

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

$baseGroups = @{}
foreach ($process in $byId.Values) {
    $baseGroups[$process.Id] = & {
        $member = $process
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
    }
}

$settingsDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'OttimizzatoreRAM'
$settingsFile = Join-Path $settingsDir 'associazioni.jsonl'
$approved = @{}
if ([IO.File]::Exists($settingsFile)) {
    foreach ($line in Get-Content -LiteralPath $settingsFile -Encoding UTF8) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try {
            $entry = ConvertFrom-Json -InputObject $line -ErrorAction Stop
            if ($entry.Figlio -is [string] -and $entry.Padre -is [string]) {
                $approved["$($entry.Padre)|$($entry.Figlio)"] = $true
            }
        } catch {
            Write-Warning 'Una riga non leggibile del file associazioni e stata ignorata.'
        }
    }
}

$groups = $baseGroups.Clone()
$proposals = @{}
$declined = @{}
$windowsRoot = $env:windir.TrimEnd('\') + '\'
foreach ($child in $byId.Values) {
    if (-not $child.Path -or $baseGroups[$child.Id] -ne $child.Path -or
        $child.Path.StartsWith($windowsRoot, [StringComparison]::OrdinalIgnoreCase)) { continue }
    $parent = $byId[$child.ParentId]
    if (-not $parent -or -not $parent.Path -or -not $parent.Started -or -not $child.Started -or
        $parent.Started -ge $child.Started -or $parent.Path -eq $child.Path -or
        $parent.Path.StartsWith($windowsRoot, [StringComparison]::OrdinalIgnoreCase)) { continue }
    $target = $baseGroups[$parent.Id]
    $key = "$($parent.Path)|$($child.Path)"
    if ($approved.ContainsKey($key)) {
        $groups[$child.Id] = $target
        continue
    }

    $root = $installationByPath[$parent.Path]
    if (-not $root -and -not ($parent.Product -and $parent.Publisher)) { continue }
    $app = if ($root) { $installations[$root] } else { $parent.Product }
    if ($ConfermaAssociazioni -and -not $declined.ContainsKey($key)) {
        $answer = Read-Host "Associare $($child.Path) a ${app}, quando avviato da $($parent.Path)? [s/N]"
        if ($answer -in @('s', 'si')) {
            $entry = [pscustomobject]@{ Padre = $parent.Path; Figlio = $child.Path }
            try {
                [void][IO.Directory]::CreateDirectory($settingsDir)
                $record = ConvertTo-Json -InputObject $entry -Compress
                [IO.File]::AppendAllText($settingsFile, "`n$record`n")
                $approved[$key] = $true
                $groups[$child.Id] = $target
                continue
            } catch {
                Write-Warning "Impossibile salvare l'associazione. Riprovare alla prossima rilevazione."
            }
        }
        $declined[$key] = $true
    }
    $proposals[$child.Id] = [pscustomobject]@{
        Applicazione = $app
        EseguibilePadre = $parent.Path
        EseguibileFiglio = $child.Path
    }
}

$byId.Values | Group-Object { $groups[$_.Id] } | ForEach-Object {
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
    $report = [ordered]@{
        Applicazione = $label
        Eseguibile = $reference.Path
        CartellaInstallazione = $installation
        Processi = @($members | Sort-Object Id | Select-Object Id, Name, Path, Bytes)
        ProposteAssociazione = @($members | ForEach-Object { $proposals[$_.Id] } | Where-Object { $_ })
        RamPrivataByte = $totalBytes
        TotaleParziale = $measured.Count -lt $members.Count
    }
    if ($usaSoglia) {
        $report.CaricoRamPercento = if ($null -ne $caricoRam) { [math]::Round($caricoRam, 2) }
        $report.SogliaRamPercento = $SogliaRamPercento
        $eligible = $installation -and $totalBytes -gt 0 -and -not $report.TotaleParziale
        if ($eligible) {
            foreach ($item in $members) {
                if ($installationByPath[$item.Path] -ne $installation -or
                    $item.Path.StartsWith($windowsRoot, [StringComparison]::OrdinalIgnoreCase)) {
                    $eligible = $false
                    break
                }
            }
        }
        $report.DaValutare = [bool]($null -ne $caricoRam -and $caricoRam -gt $SogliaRamPercento -and $eligible)
    }
    [pscustomobject]$report
} | Sort-Object RamPrivataByte -Descending
