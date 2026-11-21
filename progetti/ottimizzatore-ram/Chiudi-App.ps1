# Esempio: .\Chiudi-App.ps1 -IdProcesso 1234
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$IdProcesso
)

$ErrorActionPreference = 'Stop'

function Get-RamDisponibileByte {
    try {
        $os = Get-CimInstance Win32_OperatingSystem -Property FreePhysicalMemory
        if ($null -ne $os.FreePhysicalMemory) { [uint64]$os.FreePhysicalMemory * 1KB }
    } catch [Microsoft.Management.Infrastructure.CimException] {
        Write-Warning 'RAM disponibile di sistema non misurabile.'
    }
}

if ($IdProcesso -eq $PID) { throw 'Seleziona un processo diverso da questa sessione PowerShell.' }

$app = Get-Process -Id $IdProcesso
try {
    $started = $app.StartTime
    $name = $app.ProcessName
    $path = $app.Path
    $window = $app.MainWindowHandle
    $windowsRoot = $env:windir.TrimEnd('\') + '\'
    if (-not $path -or -not $started -or $app.SessionId -ne [Diagnostics.Process]::GetCurrentProcess().SessionId -or
        $path.StartsWith($windowsRoot, [StringComparison]::OrdinalIgnoreCase) -or $window -eq [IntPtr]::Zero) {
        throw 'Il processo non ha una finestra accessibile in questa sessione o si trova nella cartella di Windows.'
    }

    $answer = Read-Host "Chiedere la chiusura normale di $($app.ProcessName) (PID $IdProcesso), $path? [s/N]"
    if ($answer -notin @('s', 'si')) { return }

    $prima = Get-RamDisponibileByte
    $app.Refresh()
    if ($app.HasExited -or $app.StartTime -ne $started -or $app.Path -ne $path -or $app.MainWindowHandle -ne $window) {
        throw 'Il processo o la finestra sono cambiati. Ripeti la selezione.'
    }
    if (-not $app.CloseMainWindow()) {
        Write-Warning 'Windows non ha accettato la richiesta di chiusura.'
        return
    }
    if (-not $app.WaitForExit(15000)) {
        Write-Warning 'Il processo e ancora attivo. Controlla se la finestra chiede di salvare o confermare.'
        return
    }

    $dopo = Get-RamDisponibileByte
    [pscustomobject]@{
        Applicazione = $name
        IdProcesso = $IdProcesso
        RamDisponibilePrimaMiB = $(if ($null -ne $prima) { [math]::Round($prima / 1MB, 1) })
        RamDisponibileDopoMiB = $(if ($null -ne $dopo) { [math]::Round($dopo / 1MB, 1) })
        VariazioneRamDisponibileMiB = $(if ($null -ne $prima -and $null -ne $dopo) {
            [math]::Round(([decimal]$dopo - $prima) / 1MB, 1)
        })
    }
} finally {
    $app.Dispose()
}
