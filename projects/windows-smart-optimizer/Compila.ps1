param([switch]$Test)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:windir 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Compilatore .NET Framework 4.x non trovato.' }

$exe = Join-Path $PSScriptRoot 'WindowsSmartOptimizer.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.ServiceProcess.dll "/out:$exe" (Join-Path $PSScriptRoot 'WindowsSmartOptimizer.cs')
if ($LASTEXITCODE -ne 0) { throw "Build non riuscita, codice $LASTEXITCODE." }
if ($Test) {
    foreach ($iteration in 1..3) {
        $folder = Join-Path ([IO.Path]::GetTempPath()) ('optimizer-test-' + [guid]::NewGuid().ToString('N'))
        [void][IO.Directory]::CreateDirectory($folder)
        $process = Start-Process -FilePath $exe -ArgumentList @('--self-test', ('"' + $folder + '"')) -WindowStyle Hidden -PassThru
        try {
            if (-not $process.WaitForExit(30000)) { throw "Tempo scaduto. Tracce: $folder" }
            Get-Content -LiteralPath (Join-Path $folder 'result.txt')
            if ($process.ExitCode -ne 0) { throw "Test $iteration fallito. Tracce: $folder" }
        } finally { $process.Dispose() }
        Remove-Item -LiteralPath (Join-Path $folder 'result.txt'), (Join-Path $folder 'GameFixture.exe')
        [IO.Directory]::Delete($folder, $false)
    }
}
Get-Item -LiteralPath $exe | Select-Object FullName, Length
