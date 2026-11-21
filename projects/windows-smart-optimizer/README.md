# Windows Smart Optimizer

Primo incremento funzionante per Windows. L'app campiona RAM fisica, memoria impegnata, cache, CPU, disco e letture per hard fault. L'inventario di processi e servizi è consultabile su richiesta. Il campionamento avviene ogni tre secondi a riposo e ogni secondo durante il gioco rilevato.

## Avvio

- Compilare nella cartella del progetto con `powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Compila.ps1 -Test`.
- Aprire `WindowsSmartOptimizer.exe`. La build usa il compilatore .NET Framework già disponibile sul PC di prova. L'eseguibile è portatile e usa WinForms e le API Windows.
- Scegliere il file eseguibile del gioco. Per provare l'intervento, avviare un'app secondaria dalla dashboard, spuntare entrambe le opzioni e scegliere un profilo operativo. Il gioco viene rilevato in base al percorso e alla sessione.

## Policy e ripristino

| Profilo | Condizione |
| --- | --- |
| Conservativo | Simula l'intervento e mostra la proposta |
| Bilanciato | Gioco attivo e CPU almeno al 70% in due campioni consecutivi |
| Aggressivo | Gioco attivo, con l'intervento esplicitamente abilitato |

L'unica azione modifica la priorità, da Normale a Inferiore al normale, di un'app secondaria avviata dalla dashboard. Richiede che il processo sia accessibile con i permessi dell'utente. Un'app che deve completare un'attività in tempo può rallentare. La dashboard tenta il ripristino dopo 15 secondi e sospende ulteriori interventi fino alla prossima sessione di gioco. La policy lascia fuori gli altri processi e servizi e gli eseguibili selezionati nella cartella di Windows.

Prima dell'intervento viene salvato un registro pendente con PID, ora di avvio e percorso. La priorità originale di quest'azione è sempre Normale. Il ripristino verifica l'identità e la priorità corrente. Avviene all'uscita dal gioco, con il pulsante Ripristina, alla disattivazione della policy e alla chiusura della dashboard. Un processo di sorveglianza attende la fine della dashboard e tenta il ripristino. In caso d'interruzione di entrambi, la successiva apertura tenta il recupero dal registro. Lo stato locale risiede in `%LOCALAPPDATA%\Matrix\WindowsSmartOptimizer`.

L'interfaccia mostra la RAM disponibile prima e dopo il ripristino. `misure.tsv` conserva campioni appaiati di RAM disponibile e memoria impegnata. Sono misure di sistema, influenzate anche dagli altri programmi. Le letture per hard fault sono un indicatore di paging e non equivalgono al numero totale di hard fault.

## Moduli

- `Telemetry` e `IoCounters` leggono i contatori Windows.
- `Dashboard` mostra i dati, applica le condizioni dei profili e rileva il gioco configurato.
- `PriorityAction` gestisce scrittura anticipata e ripristino. La modalità interna `--watch` recupera lo stato dopo l'uscita del proprietario.

## Prove e criteri ancora aperti

- Build Windows x64, versione 10.0.19045, 9,98 GiB visibili. Eseguibile da 26.112 byte. SHA-256 `2143BD3E95F6DDE013C4BB66149B65CF38B213234522ECAC62614252F9DB1DD0`.
- Tre esecuzioni di `-Test` riuscite, con verifica delle letture di base, rilevamento e arresto del gioco sintetico, variazione e ripristino della priorità, recupero del registro e percorso di recupero dopo l'arresto del proprietario. I test usano processi creati apposta.
- Dashboard avviata e reattiva nella build precedente al limite temporale. A riposo, in una rilevazione breve, 33,59 MiB di working set e 9,9 MiB di working set privato secondo il contatore Windows. Nessun incremento di CPU osservato nella finestra di circa sei secondi. Il dato va ripetuto sotto carico.
- Benchmark sintetico: due carichi sullo stesso core, con campioni di 1,25 secondi. Il primo carico ha ricevuto il 47,9%, 53,9% e 51,9% della CPU attribuita ai due carichi a pari priorità. Con la riduzione dell'altro carico, il primo ha ricevuto il 100% in tutti e tre i campioni. La priorità è stata ripristinata in tutti i casi. Questo evidenzia il rischio di rallentare il carico secondario e ha motivato il limite temporale. Non misura FPS o latenza di un gioco. `Benchmark.cs` si compila insieme a `WindowsSmartOptimizer.cs` con le stesse referenze di `Compila.ps1`, aggiungendo `/main:Benchmark`, usando `/target:exe` e passando al binario una cartella temporanea vuota.
- Rimangono da verificare il collaudo su un gioco reale e su hardware rappresentativo da 4 GB, il confronto indipendente sistematico della telemetria e il ripristino dopo un riavvio reale. Da completare la mappa delle dipendenze del gioco, l'adattamento basato su benefici ripetibili e le misure comparabili di latenza e FPS. Il prototipo non certifica benefici di prestazioni.
