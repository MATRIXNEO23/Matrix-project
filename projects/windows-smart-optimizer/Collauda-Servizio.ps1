param([string]$Fixture,[string]$ServiceName,[string]$Result,[string]$ExpectedHash,[string]$Adapter,[string]$AdapterHash)
$ErrorActionPreference='Stop'
$registered=$false
$controller=$null
$serviceBinary=$null
$journal=$null
$adapterBinary=$null
$folder=$null
$ownedFolder=$false
$log=[System.Collections.Generic.List[string]]::new()
$exitCode=1
try {
 if ($ServiceName -notmatch '^MatrixOptimizerTest-[0-9a-f]{32}$') {throw 'Identita del servizio non valida.'}
 $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
 if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Il processo non ha ricevuto privilegi amministrativi.'}
 if(Get-Service -Name $ServiceName -ErrorAction SilentlyContinue){throw 'Il nome del servizio esiste gia.'}
 $folder=Join-Path $env:ProgramData $ServiceName
 New-Item -ItemType Directory -Path $folder -ErrorAction Stop | Out-Null
 $ownedFolder=$true
 $serviceBinary=Join-Path $folder 'ServiceFixture.exe'
 Copy-Item -LiteralPath $Fixture -Destination $serviceBinary
 if((Get-FileHash -LiteralPath $serviceBinary -Algorithm SHA256).Hash -ne $ExpectedHash){throw 'Hash della fixture non verificato.'}
 if($serviceBinary -match '\s'){throw 'Percorso amministrativo inatteso.'}
 $cmd="$serviceBinary $ServiceName"
 & sc.exe create $ServiceName 'binPath=' $cmd 'start=' demand 'obj=' 'NT AUTHORITY\LocalService' | ForEach-Object { $log.Add("SCM: $_") }
 if($LASTEXITCODE -ne 0){throw "Registrazione fallita: $LASTEXITCODE"}
 $registered=$true

 Add-Type -AssemblyName System.ServiceProcess
 $controller=[System.ServiceProcess.ServiceController]::new($ServiceName)
 $timeout=[TimeSpan]::FromSeconds(15)
 $controller.Start()
 $controller.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running,$timeout)
 if($controller.DependentServices.Count){throw 'Servizio di prova con dipendenti inattesi.'}
 $key="HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
 $type=Get-ItemPropertyValue -LiteralPath $key -Name Start
 if($type -ne 3){throw 'Tipo di avvio inatteso.'}
 if($Adapter){
  $adapterBinary=Join-Path $folder 'ServiceAdapterProbe.exe'
  Copy-Item -LiteralPath $Adapter -Destination $adapterBinary
  if((Get-FileHash -LiteralPath $adapterBinary -Algorithm SHA256).Hash -ne $AdapterHash){throw 'Hash del collaudo adapter non valido.'}
  & $adapterBinary $ServiceName $folder | ForEach-Object {$log.Add("Adapter: $_")}
  if($LASTEXITCODE -ne 0){throw "Collaudo adapter fallito: $LASTEXITCODE"}
  if((Get-ItemPropertyValue -LiteralPath $key -Name Start) -ne $type){throw 'Tipo di avvio cambiato dopo il test adapter.'}
 }

 $journal=Join-Path $folder 'restore.txt'
 $bytes=[Text.Encoding]::UTF8.GetBytes("$ServiceName|Running|$type")
 $stream=[IO.FileStream]::new($journal,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::WriteThrough)
 try {$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)} finally {$stream.Dispose()}
 $controller.Stop()
 $controller.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped,$timeout)
 $log.Add('Verificato: servizio di prova Running -> Stopped.')
 $record=([IO.File]::ReadAllText($journal)).Split('|')
 if($record.Count -ne 3 -or $record[0] -ne $ServiceName -or $record[1] -ne 'Running' -or [int]$record[2] -ne $type){throw 'Journal non valido.'}
 $controller.Dispose()
 $controller=[System.ServiceProcess.ServiceController]::new($record[0])
 $controller.Start()
 $controller.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running,$timeout)
 if((Get-ItemPropertyValue -LiteralPath $key -Name Start) -ne $type){throw 'Tipo di avvio cambiato.'}
 $log.Add('Verificato: journal riletto, servizio Stopped -> Running, tipo Manuale invariato.')
 $exitCode=0
} catch {
 $log.Add("Errore: $($_.Exception.Message)")
} finally {
 if($registered){
  try {
   if(!$controller) {$controller=[System.ServiceProcess.ServiceController]::new($ServiceName)}
   $controller.Refresh()
   if($controller.Status -eq [System.ServiceProcess.ServiceControllerStatus]::StartPending){
    $controller.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(12))
   }
   if($controller.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped){
    $controller.Stop()
    $controller.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(12))
   }
  }catch{$log.Add("Pulizia: $($_.Exception.Message)");$exitCode=1}
 }
 if($controller){$controller.Dispose()}
 if($registered){
  & sc.exe delete $ServiceName | ForEach-Object {$log.Add("SCM: $_")}
  if($LASTEXITCODE -ne 0 -or (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)){$log.Add('Rimozione non verificata.');$exitCode=1}
  else {$log.Add('Servizio temporaneo rimosso.')}
 }
 if($ownedFolder -and -not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)){
  foreach($file in @($journal,$serviceBinary,$adapterBinary,(Join-Path $folder 'search.pending'))){
   if($file -and [IO.File]::Exists($file)){
    try {[IO.File]::Delete($file)}catch{$log.Add("Pulizia file: $($_.Exception.Message)");$exitCode=1}
   }
  }
  if([IO.Directory]::Exists($folder)){
   try {[IO.Directory]::Delete($folder,$false)}catch{$log.Add("Pulizia cartella: $($_.Exception.Message)");$exitCode=1}
  }
 }
 try {[IO.File]::WriteAllLines($Result,$log)}catch{Write-Error $_}
}
exit $exitCode
