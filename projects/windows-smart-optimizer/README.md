# Windows Smart Optimizer

Primo incremento funzionante per Windows. L'app campiona RAM fisica, memoria impegnata, cache, CPU, disco e letture per hard fault. L'inventario di processi e servizi è consultabile su richiesta. Il campionamento avviene ogni tre secondi a riposo e ogni secondo durante il gioco rilevato.

## Avvio

- Compilare nella cartella del progetto con `powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Compila.ps1 -Test`.
- Aprire `WindowsSmartOptimizer.exe`. La build usa il compilatore .NET Framework già disponibile sul PC di prova. L'eseguibile è portatile e usa WinForms e le API Windows.
- Scegliere il file eseguibile del gioco. Per provare l'intervento, avviare un'app secondaria dalla dashboard, spuntare entrambe le opzioni e scegliere un profilo operativo. Il gioco viene rilevato nella stessa sessione in base al percorso o all'identità del file Windows, se l'installazione espone due percorsi per lo stesso eseguibile.

## Policy e ripristino

| Profilo | Condizione |
| --- | --- |
| Conservativo | Simula l'intervento e mostra la proposta |
| Bilanciato | Gioco attivo e CPU almeno al 70% in due campioni consecutivi |
| Aggressivo | Gioco attivo, con l'intervento esplicitamente abilitato |

L'unica azione modifica la priorità, da Normale a Inferiore al normale, di un'app secondaria avviata dalla dashboard. Richiede che il processo sia accessibile con i permessi dell'utente. Un'app che deve completare un'attività in tempo può rallentare. La dashboard tenta il ripristino dopo 15 secondi e sospende ulteriori interventi fino alla prossima sessione di gioco. La policy lascia fuori gli altri processi e servizi e gli eseguibili selezionati nella cartella di Windows.

Prima dell'intervento viene salvato un registro pendente con PID, ora di avvio e percorso. La priorità originale di quest'azione è sempre Normale. Il ripristino verifica l'identità e la priorità corrente. Avviene all'uscita dal gioco, con il pulsante Ripristina, alla disattivazione della policy e alla chiusura della dashboard. Un processo di sorveglianza attende la fine della dashboard e tenta il ripristino. In caso d'interruzione di entrambi, la successiva apertura tenta il recupero dal registro. Lo stato locale risiede in `%LOCALAPPDATA%\Matrix\WindowsSmartOptimizer`.

La spunta della scorciatoia registra Ctrl+Alt+F10 nella sessione Windows. Il comando viene ignorato se il gioco configurato non è rilevato, e lo spegnimento del boost avvia subito il ripristino. I profili e l'autorizzazione all'intervento restano applicabili. La scorciatoia viene liberata alla chiusura. Verificati registrazione e rilascio su Windows, transizioni del controllo e ripristino con processi sintetici. Explorer e Microsoft Defender sono esclusi dalle azioni.

L'interfaccia mostra la RAM disponibile prima e dopo il ripristino. `misure.tsv` conserva campioni appaiati di RAM disponibile e memoria impegnata. Sono misure di sistema, influenzate anche dagli altri programmi. Le letture per hard fault sono un indicatore di paging e non equivalgono al numero totale di hard fault.

## Moduli

- `Telemetry` e `IoCounters` leggono i contatori Windows.
- `Dashboard` mostra i dati, applica le condizioni dei profili e rileva il gioco configurato.
- `PriorityAction` gestisce scrittura anticipata e ripristino. La modalità interna `--watch` recupera lo stato dopo l'uscita del proprietario.

## Prove e criteri ancora aperti

- Build Windows x64, versione 10.0.19045, 9,98 GiB visibili. Eseguibile da 28.672 byte. SHA-256 `EC1E434A56D971FDF3747CC57E35DFD09887E6E10890450EAB8D152ED7F35491`.
- Tre esecuzioni di `-Test` riuscite, con verifica delle letture di base, rilevamento e arresto del gioco sintetico, variazione e ripristino della priorità, recupero del registro e percorso di recupero dopo l'arresto del proprietario. I test usano processi creati apposta.
- Dashboard avviata e reattiva nella build precedente al limite temporale. A riposo, in una rilevazione breve, 33,59 MiB di working set e 9,9 MiB di working set privato secondo il contatore Windows. Nessun incremento di CPU osservato nella finestra di circa sei secondi. Il dato va ripetuto sotto carico.
- Benchmark sintetico: due carichi sullo stesso core, con campioni di 1,25 secondi. Il primo carico ha ricevuto il 47,9%, 53,9% e 51,9% della CPU attribuita ai due carichi a pari priorità. Con la riduzione dell'altro carico, il primo ha ricevuto il 100% in tutti e tre i campioni. La priorità è stata ripristinata in tutti i casi. Questo evidenzia il rischio di rallentare il carico secondario e ha motivato il limite temporale. Non misura FPS o latenza di un gioco. `Benchmark.cs` si compila insieme a `WindowsSmartOptimizer.cs` con le stesse referenze di `Compila.ps1`, aggiungendo `/main:Benchmark`, usando `/target:exe` e passando al binario una cartella temporanea vuota.
- Prova con Minecraft for Windows 1.26.6501.0. Il rilevatore ha associato l'eseguibile selezionato in XboxGames al processo avviato da WindowsApps, dopo aver verificato che i percorsi puntano allo stesso file. La prova ha rilevato l'uscita normale di Minecraft e verificato il ripristino della priorità di un processo sintetico separato. La prova si è limitata alla finestra del gioco, senza aprire mondi.
- Rimangono da verificare il comportamento durante una partita e su hardware rappresentativo da 4 GB, il confronto indipendente sistematico della telemetria e il ripristino dopo un riavvio reale. Da completare la mappa delle dipendenze del gioco, l'adattamento basato su benefici ripetibili e le misure comparabili di latenza e FPS. Il prototipo non certifica benefici di prestazioni.

### Cattura fotogrammi con Minecraft

Intel PresentMon 2.6.0, Minecraft in finestra sul PC Windows 10 da 9,98 GiB. Due coppie da otto secondi, nell'ordine spento/acceso e acceso/spento. La finestra è rimasta in primo piano per le quattro catture valide. Una cattura iniziale è stata scartata per perdita del primo piano. La scena esatta non è stata verificata dall'automazione. L'intervento ha ridotto soltanto la priorità di un carico CPU sintetico, ripristinata dopo ciascun passaggio.

| Prova | FPS visualizzati spento → acceso | 95° percentile del tempo fra fotogrammi spento → acceso |
| --- | ---: | ---: |
| 1 | 51,43 → 58,37 | 33,32 → 16,69 ms |
| 2 | 56,59 → 55,84 | 33,31 → 33,32 ms |

Gli FPS derivano dalla media degli intervalli di cambio immagine della catena di presentazione di Minecraft. Il 99° percentile è rimasto intorno a 33,34 ms. L'effetto positivo della prima coppia non si è ripetuto. Questa prova breve non dimostra un aumento stabile di FPS o fluidità e non misura la latenza di input.

### Ripetizione durante la partita

L'utente ha confermato di essere nel mondo. Due ulteriori coppie da otto secondi, Minecraft in primo piano, ordine spento/acceso e acceso/spento. La priorità è stata ridotta e ripristinata soltanto per il carico CPU sintetico. L'inquadratura ferma è stata richiesta e non verificata visivamente.

| Prova | FPS visualizzati spento → acceso | Intervalli di almeno 25 ms spento → acceso |
| --- | ---: | ---: |
| 1 | 59,88 → 56,47 | 1 → 27 |
| 2 | 49,90 → 57,86 | 80 → 17 |

Minecraft ha presentato 59,78–59,97 FPS in tutti e quattro i campioni. La variabilità maggiore è stata negli intervalli tra immagini visualizzate. Una coppia è peggiorata e una è migliorata. Questo test non giustifica abilitare automaticamente la riduzione di priorità per Minecraft nelle condizioni osservate.

## Incremento Game Booster reversibile

L'ordine [Game Booster](ORDINE_WORK_GAME_BOOSTER_OTTIMIZZAZIONE_REVERSIBILE.md) trova qui una prima integrazione. La build corrente Windows 10 x64 è di 44.544 byte. SHA-256 `3D655D49D06EC84C31D3BC15916EBC7369D51851D4BC7FEDE502292CA19E154B`. I valori nelle sezioni precedenti descrivono build storiche.

- **Discovery:** `MachineInventory` legge hardware e driver, servizi, processi, browser noti, chiavi Run e attività visibili nella radice dell'Utilità di pianificazione. La scansione singola avviene fuori dal thread UI. La ricerca giochi individua Minecraft for Windows nelle installazioni XboxGames. Il selettore esistente supporta eseguibili manuali e portatili.
- **Policy e UI:** il gioco individuato viene proposto con profilo Conservativo e boost spento. I profili salvano livello e autorizzazione all'intervento per percorso del gioco. Import ed export includono le sole opzioni e l'import tiene spento il boost. Comandi di attivazione, disattivazione e ripristino d'emergenza sono distinti. Il cambio di gioco passa prima dal ripristino. La scorciatoia resta opzionale.
- **Transazione:** il journal dura su disco prima della modifica e include ID, PID, ora di avvio, percorso, valore originale, valore applicato e scadenza. Resta leggibile il journal precedente a tre campi. App e watchdog serializzano gli accessi. La UI attende il segnale di disponibilità del watchdog prima di agire. Il watchdog controlla la scadenza anche con la dashboard ancora attiva. Il rollback conserva un valore di priorità diverso da quello scritto dal booster.
- **Distribuzione:** compilazione e tre prove ripetibili da `Compila.ps1 -Test`. Per il benchmark storico compilare anche `MachineInventory.cs` e aggiungere la referenza a `System.Management.dll`.

| Funzione storica | Esito e prerequisito | Default | Impatto, rischio e ripristino |
| --- | --- | --- | --- |
| Processi / priorità | Verificata su Windows 10 con processi di prova. Solo app secondaria avviata dal booster, stessa sessione utente, priorità normale. | Conservativo; boost spento | Riduce la CPU disponibile all'app secondaria, fino alla sua temporanea quasi inattività sotto contesa. Ripristino automatico e manuale; benefici in gioco non ripetuti. |
| Servizi Windows e terze parti, startup, task | Inventario parziale in lettura. Dipendenze, arresto e ripristino specifici da verificare. | Spento | Nessun cambiamento. |
| Browser e Explorer | Diagnosi. Le sessioni sono da preservare. | Spento | Sospensione e recupero delle finestre non verificati. |
| Piano energetico, affinità, worker threads, cache, Prefetch, rete, GUI, Fix bad tweak | Supporto ed effetto storico da verificare voce per voce. | Spento | Nessuna scrittura. La pulizia indiscriminata di cache e appunti resta esclusa. |
| GPU | Diagnosi Radeon HD 6450 con driver 15.301.1901.0. API di regolazione, sensori e undo non verificati. | Sola diagnosi | Nessuna calibrazione, modifica delle frequenze o stress test. |
| Defender, firewall, servizi di sicurezza | Esclusi. | Esclusi | Nessuna modifica alle protezioni. |

### Verifiche dell'incremento

Windows 10 19045, Intel Core i3-2100, Radeon HD 6450. Inventario reale: 1 gioco rilevato, 175 processi, 270 servizi, 8 voci Run. Test riusciti per due profili indipendenti, aggiornamento e rilettura, ripristino dopo la scadenza a 15,3 secondi con proprietario attivo, modifica concorrente preservata, uscita/crash sintetici e journal. La build finale ha superato tre test e ha aperto una dashboard reattiva a boost spento. Campione breve dopo la scansione: 12,41 MiB di RAM privata residente e 0,73% di un core. Non è una misura a regime su un PC con 4 GB.

Restano da completare la libreria multigioco e Steam, il riconoscimento delle dipendenze e degli avvii figli, l'inventario esaustivo, i candidati preselezionati voce per voce, gli adapter aggiuntivi con undo verificato, i test di spegnimento improvviso e di privilegi, il catalogo storico completo e nuovi benchmark ripetuti. L'auto-tuning è bloccato sul PC di prova finché non sono verificati hardware, API e ripristino.

### Estensione libreria, dipendenze e diagnosi

Build Windows 10 x64: 58.368 byte, SHA-256 `F637E4BC7DC0D58B609589AF26FF9FF18FE9D9C7264C3FAC75668F2D51EB3942`.

- **Libreria e albero:** rileva Minecraft e legge i manifest Steam sia nella libreria principale sia nelle librerie locali registrate. Per una voce Steam, l'utente sceglie l'eseguibile nella cartella rilevata. Il monitor riconosce l'avvio del file selezionato indipendentemente dal launcher e segue i discendenti osservati che appartengono alla stessa sessione e alla cartella dell'eseguibile. Quando il processo principale termina, la sessione resta attiva finché uno di questi figli è ancora in esecuzione. Dipendenze esterne, avvii attraverso broker e riconoscimento automatico dell'eseguibile Steam restano aperti.
- **Inventario:** attività pianificate nelle sottocartelle, fino a 64 cartelle o 512 voci. Nel test reale: 1 gioco, 270 servizi, 94 attività. Per Steam il test è sintetico. La voce Steam nel registro locale non ha fornito manifest utilizzabili.
- **GPU:** la scheda Radeon HD 6450 e il driver 15.301.1901.0 espongono AMD ADL Overdrive 5. Il driver segnala la regolazione disabilitata. La build ha letto 650 MHz core, 820 MHz memoria e 50,5 °C nel campione. Il valore di utilizzo era 29%. L'integrazione usa solo letture dalla DLL di sistema. Non è stata provata una scrittura reversibile e la calibrazione resta disabilitata.
- **Edge:** prova headless con profilo separato e pagina locale sintetica. Il primo campione è stato scartato perché il renderer non è rimasto stabile. Nel campione valido abbassare la priorità della radice non ha cambiato la priorità osservata del renderer, rimasta a 8. Il tempo CPU del renderer è passato da 609,4 a 625,0 ms fra le finestre misurate. La radice è tornata a priorità Normale. Il dato non evidenzia un beneficio e non autorizza un adapter per Edge. Le finestre Edge personali sono rimaste fuori dal test.
- **Test:** tre esecuzioni di `Compila.ps1 -Test` sulla build finale. Verificate anche la lettura di un manifest Steam sintetico, l'esclusione di un percorso di installazione non valido e la gestione del processo figlio dopo l'uscita della radice.

### Condizioni per completare l'ordine

Mancano la preselezione delle singole voci basata su benefici ripetibili, il catalogo storico completo, le prove reali su Steam e 4 GB, la verifica del riavvio a freddo e la calibrazione GPU. Servizi, piano energetico, affinità, browser e shell restano senza adapter attivo finché ciascuna modifica non ha un ripristino verificato e una misura di beneficio. Per la GPU occorrono driver e hardware su cui validare la lettura, il limite termico e il recupero dopo una modifica, poi consenso specifico alla calibrazione. Per servizi e shell occorre un ambiente sacrificabile che consenta anche il test di errore e interruzione. Le prove Minecraft già registrate non hanno mostrato un miglioramento ripetibile.

### Ripristino in condizioni di errore

Build di 59.904 byte, SHA-256 `B5A3442C63846A871766AB3EED7717FA8A75ACB6DD9DBB42E7029C2174683C88`. In tre esecuzioni su Windows 10 Home 22H2, i nuovi test mantengono il journal corrotto per diagnosi, impediscono di cambiare priorità se il journal non può essere scritto, ignorano registri riferiti a un PID con ora di avvio o percorso diversi e ripristinano ancora un journal valido del formato precedente. I test usano soltanto processi creati dalla fixture.

Sul PC, virtualizzazione firmware, EPT e VM Monitor Mode risultano disponibili. Windows Sandbox non è supportato nell'edizione Home. Non è stato trovato un hypervisor già predisposto. Restano senza collaudo in una vera VM i test su servizi, shell e riavvio a freddo. La RAM di una VM configurata a 4 GB consente test funzionali, ma non rappresenta da sola la fluidità su hardware fisico con 4 GB.

### Selettore applicazioni e confronto con Game Booster 3

- **Selezione:** Scegli gioco e Avvia app secondaria aprono un elenco cercabile di giochi, programmi registrati e app nella cartella virtuale di Windows. Consente un .exe manuale se Windows non fornisce un percorso verificabile. Sul PC sono state rilevate 194 voci installate e 1 gioco. L'elenco si aggiorna quando la scansione asincrona termina.
- **Azioni:** la finestra Azioni Boost mostra disponibilità per categoria, stato dell'intervento e priorità corrente. La finestra di prova permette di vedere la transizione di priorità e il ritorno al valore originale senza chiudere app. Una sola azione è operativa. L'arresto e la riapertura generici delle app non sono implementati.
- **REA:** analisi statica di `GameBooster.exe`, `Boost.exe`, `gbtray.exe`, `taskMgr.dll` e `PowerConfig.dll`. Le risorse mostrano opzioni per processi, servizi Windows e di terzi, Explorer, memoria, appunti, piano energetico e ripristino a fine gioco. In `Boost.exe` e `GameBooster.exe` risultano importazioni per controllo e avvio servizi, dipendenze, terminazione processi e working set. `PowerConfig.dll` contiene nomi delle API di lettura e cambio piano. Le importazioni e i riferimenti statici non stabiliscono le condizioni esatte del ripristino o la conservazione delle sessioni. IObit non è stato avviato durante l'analisi.
- **Build locale:** 71.680 byte, SHA-256 `D9E86E521CD7F64E80EFD5BE7585744684B0E86EE64BAC66D3665BDB2A996525`. Tre test integrati passati. Dashboard reattiva, 15,07 MiB di RAM privata residente nel campione dopo la scansione con boost spento. Non è stato misurato il consumo di IObit in condizioni comparabili.

### Profilo del PC e modalità Esperto

La dashboard espone un comando Boost principale e il pulsante Profilo di questo PC. Il profilo consigliato per la macchina collaudata (Core i3-2100 e Radeon HD 6450) è Conservativo. I confronti Minecraft non hanno mostrato un vantaggio ripetibile. Il profilo conserva il piano Bilanciato osservato e i valori Windows correnti; nessun tweak al registro viene applicato. La modalità Esperto legge le chiavi effettive, pubblica valore corrente e proposta e indica quando un valore manca o un tweak storico non è stato mappato. Nella prova il valore corrente dello scheduler era 26.

L'opzione temporanea Prestazioni elevate è separata dal profilo consigliato e attivabile esplicitamente in Azioni Boost. Richiede un gioco rilevato, un profilo operativo, scrittura anticipata del journal e watchdog pronto. Registra piano originale, piano applicato, identificatore e scadenza, poi verifica il cambio tramite PowrProf. La disattivazione, la chiusura del gioco, la scadenza e il recupero all'avvio attivano il percorso di ripristino. Se il piano corrente è diverso da quello applicato, il rollback lascia la scelta successiva. Le due azioni di ripristino, priorità ed energia, vengono tentate separatamente.

Test reale del singolo adapter: Bilanciato → Prestazioni elevate → Bilanciato, con nuova istanza del registro per il ripristino. Verifica indipendente con `powercfg /getactivescheme` a fine test. Il test concorrente simulato ha preservato Bilanciato. Un eventuale beneficio FPS del cambio piano, la sua scadenza di 15 secondi sotto arresto del processo reale e il riavvio a freddo restano da misurare per questo adapter. La build locale di 82.944 byte è stata aperta a boost spento dopo tre prove integrate riuscite. SHA-256 `7BAC3965B6B536F2F5B659F427094DB2C38C8AC02B03858FC32E8CAF7F87765A`.

### Finestre durante il boost

La spunta Minimizza e ripristina in Azioni Boost riguarda la finestra principale dell'app secondaria avviata dalla dashboard. Richiede il gioco attivo, un profilo operativo e il watchdog pronto. Prima dell'intervento registra l'identità del processo e della finestra, lo stato e la geometria. Il ripristino usa la finestra originale e lascia com'è una finestra che l'utente ha già riportato a schermo. La disattivazione e la fine della sessione ripristinano priorità, piano e finestra; la scadenza della sola priorità lascia proseguire le altre azioni. Una finestra chiusa dall'utente non viene ricreata.

Su una finestra di prova Windows sono stati osservati minimizzazione, stesso processo, ritorno alla geometria originale e rispetto del ripristino manuale. Tre prove integrate passate. Il valore NTFS 0x80000002 appare come Sistema · disabilitato, come confermato da fsutil. Build aperta a boost spento, 89.600 byte, SHA-256 `BE7DCF916D0DC9CEE9D99033D11C70490F76CBD2B39E3E7F6674E934455CA232`.

Quest'azione preserva la sessione di una finestra. Mancano ancora gli adapter per disabilitare processi o servizi con ripristino verificato, chiudere e riaprire app, e applicare automaticamente impostazioni Windows calibrate tramite benchmark. Il profilo consigliato conserva i valori correnti finché non emerge un vantaggio ripetibile.

### Avvio automatico per programmi spuntati

Avvio automatico mostra un elenco di eseguibili con spunte. La lista include i giochi e le app per cui è disponibile un eseguibile selezionabile; si può aggiungere un .exe locale dalla scelta manuale. Il comando Salva conserva le spunte e l'interruttore generale per l'utente corrente. L'opzione è spenta per impostazione predefinita; funziona mentre la dashboard è aperta, senza avviare programmi o registrare un avvio all'accesso a Windows.

Quando viene rilevato un eseguibile spuntato, la dashboard carica il profilo del gioco e abilita il boost. Il profilo Conservativo fa solo anteprima. Il rilevamento segue una sessione per volta e attende l'uscita delle dipendenze già riconosciute. Il boost si disattiva e ripristina le azioni quando la sessione finisce. La disattivazione manuale resta valida finché il gioco resta aperto. Se viene riconosciuto un ripristino pendente dopo un arresto, l'avvio automatico resta sospeso per l'istanza corrente. I profili v2 salvano anche le opzioni energia e finestre; il formato v1 resta leggibile e mantiene queste opzioni disattivate.

Verifica con fixture sul PC: programma non spuntato ignorato; programma spuntato riconosciuto con il suo profilo; alla sua uscita, boost spento e priorità della fixture riportata a Normale. Tre test integrati riusciti; dashboard finale aperta, build 96.768 byte, SHA-256 `3A54B7C06C045A8BCA9684C7E683927A2AD57AB42F0FFE25D515DE1FE46E3FDC`.

La finestra Avvio automatico usa ora una lista con caselle sempre visibili accanto al nome e si popola anche se aperta prima che finisca la scansione. Il test UI ha aperto la finestra prima della scansione, selezionato Minecraft e verificato il salvataggio in un profilo temporaneo, senza cambiare le spunte del profilo reale. La build aggiornata è stata riaperta sul PC a funzioni automatiche ancora da configurare. SHA-256 `4D317187732B99ED31DE5AA7F083A4195C946FD1B7CA60AC07545EE63D599985`.
