# ORDINE DI LAVORO — Booster gaming leggero, configurazione automatica e ripristino
Data: 2026-11-21
Stato: specifica da implementare, non modifiche già applicate.

## Mandato
Integrare il booster già in sviluppo, senza riscriverlo o rompere funzioni esistenti. Prendere come riferimento il comportamento e l'interfaccia di IObit Game Booster 3.4 (liste per Processi, Servizi Windows non necessari, Servizi di terze parti, Altri e Tweaks/Expert Mode), ma implementare soluzioni compatibili con Windows moderno e fondate su misure. Priorità: carico minimo del booster, utilità reale, piena reversibilità delle modifiche gestite.

## UX richiesta
- Alla prima apertura: scansione hardware (CPU/GPU/RAM/dischi), versione Windows, giochi e launcher, servizi Windows e di terze parti, startup app, processi, browser e task schedulati; produrre **spunte già preconfigurate per il PC**, non una lista vuota. Non applicare nulla senza l'attivazione del Boost.
- Sezioni espandibili con nome, produttore/percorso, descrizione comprensibile, impatto misurato o stimato con livello di confidenza, dipendenze, rischio, motivo della selezione, stato iniziale e possibilità di spuntare/deselezionare. Modalità base semplice, modalità Esperto completa. Prevedere selezione massiva solo per categorie sicure.
- Libreria giochi: autodiscovery e aggiunta manuale .exe, inclusi portatili; profili indipendenti per gioco, override dell'auto-configurazione, import/export. Riconoscere avvio da desktop, menu contestuale e Steam (altri launcher quando verificati), seguendo il processo di gioco e i suoi figli, non soltanto il launcher.
- Comandi distinti **Attiva Boost** e **Disattiva Boost / Ripristina PC**; il ripristino manuale deve funzionare anche mentre il gioco rimane aperto. Opzione ripristino automatico alla chiusura del gioco. Pulsante Ripristino di emergenza.

## Interventi da catalogare e verificare
1. CPU: priorità processo adatta (mai Realtime); affinità, scheduling e power profile solo se dimostrano benefici e sono reversibili. Rilevare CPU ibrida e evitare interventi che peggiorino scheduling.
2. GPU: identificare GPU effettiva e profilo applicazione, modalità alimentazione quando supportata, bottleneck CPU/GPU, frametime; non forzare overclock o hack driver. L'eventuale calibrazione volontaria e reversibile segue le condizioni della sezione Auto-tuning GPU.
3. Windows: servizi non essenziali, processi in background, avvio automatico, manutenzione/indicizzazione/sincronizzazioni e task non critici; non interrompere servizi vitali, driver, audio, networking necessario, anti-cheat, accessibilità e sicurezza. Gestire dipendenze e privilegi.
4. Terze parti: analizzare servizi e startup dei programmi installati; preselezionare solo candidati realmente superflui durante una sessione gaming, con whitelist/protezioni, riconoscimento delle dipendenze e opt-out per ogni voce.
5. Browser e finestre: individuare finestre aperte e processi; preferire sospensione sicura/reversibile e riduzione delle attività background. Non terminare browser con schede, moduli o documenti non salvati. Se sospendere può causare errori (connessioni, media, sessioni), segnalarlo e non applicare automaticamente.
6. Explorer/shell: gestione facoltativa in Esperto, con strategia di ritorno garantita e interfaccia di recupero indipendente da Explorer; default disattivato quando non è possibile garantire continuità. Salvaguardare accesso all'interfaccia del booster.
7. Catalogo Expert di Game Booster 3.4, inclusi “Fix bad tweak”, worker threads, CPU capability, delay worker thread, Prefetch, cache, rete, GUI: mappare ogni nome sul parametro reale, verificare supporto per versione Windows, testare e marcare efficace / sperimentale / obsoleto / pericoloso. Niente copia cieca di tweak legacy o modifiche permanenti. Il catalogo completo è una fase di ricerca, non un elenco già verificato.
8. Dalle schermate di riferimento: sezioni Processi, Servizi Windows non necessari, Servizi di terze parti e Altri; opzioni storiche come Virtual Desktop, pulizia RAM, appunti, piano energetico, Explorer.exe e aggiornamenti automatici. Valutarle una per una; evitare purghe RAM, cancellazioni appunti, blocchi update o modifiche shell senza prova di beneficio, affidabilità e recuperabilità. Non disabilitare Defender, antivirus, firewall, Windows Update in modo persistente o protezioni antimalware.

## Regola primaria: ripristino allo stato pre-Boost
- Prima di toccare qualsiasi impostazione creare uno snapshot transazionale durabile dei **soli elementi gestiti**: stato preciso di servizi e loro start type, PID/identità/process tree, stato sospensione, priorità, power plan originale, shell, preferenze e valori assenti/presenti. Scrivere journal prima dell'applicazione con ID sessione e passi idempotenti.
- Applicare solo interventi con undo definito e collaudato; se uno step fallisce, rollback ordinato. Consentire disattivazione Boost indipendentemente dalla fine del gioco e rollback su chiusura del gioco, crash, uscita booster, riavvio tramite recovery all'avvio. Prevedere watchdog minimo e restore d'emergenza quando possibile.
- Processi terminati e certe finestre/browser **non si possono ricostruire esattamente**: non promettere lo stato identico. Non terminare ciò che deve ritornare con finestre e sessione intatte; sospendere solo quando verificato sicuro, altrimenti lasciare attivo e spiegare perché. Ripristinare posizione/visibilità finestre dove possibile senza sovrascrivere modifiche fatte dall'utente nel frattempo.
- Riconciliare modifiche concorrenti Windows/utente; distinguere cambiamenti fatti dal booster da cambiamenti successivi; niente rollback cieco che sovrascriva nuove preferenze. Evidenziare eccezioni e offrire ripristino guidato.

## Design tecnico
- Architettura modulare: discovery/inventory; policy+compatibility; game detector; transaction engine/undo journal; UI; benchmark; diagnostics. Nessun polling continuo pesante; preferire notifiche/eventi e limiti CPU/RAM misurati.
- Permessi minimi e privilegi elevati solo per interventi necessari; logging locale contenuto e privacy. Non installare servizi permanenti se non indispensabili e autorizzati.
- Nessuna scrittura permanente a registro o servizi senza compatibilità documentata, piano undo e consenso pertinente. Preflight che interrompa il Boost se il rollback non è pronto.

## Implementazione e verifica, in ordine
A. Audit codice esistente e ambiente; matrice funzionalità/Windows, inventario dipendenze e baseline delle prestazioni.
B. Prototipo inventory + interfaccia spunte precompilate e per-gioco; test non distruttivi.
C. Journal, transazioni e ripristino crash-safe: test automatizzati e manuali prima di abilitare un solo tweak.
D. Adapter progressivi per servizi, processi, browser, shell, power plan, CPU/GPU; feature flags e blacklist.
E. Benchmark A/B ripetuti (FPS medi, 1% low, frametime, utilizzo CPU/GPU, RAM, overhead del booster, stabilità, tempo ripristino) con stessi giochi/scenari, temperature e condizioni. Promuovere interventi solo se miglioramento ripetibile e non regressivo; annotare anche test falliti.
F. Test di recupero: chiusura gioco, disattivazione manuale con gioco aperto, crash booster, crash gioco, spegnimento improvviso, privilegi insufficienti, servizi dipendenti, finestre/browser con contenuti, più giochi, cambio profilo, aggiornamento Windows e modifica concorrente. Confrontare snapshot pre/post e documentare differenze inevitabili.

## Consegne e criteri di accettazione
- Piano architetturale e tabella tweak (nome storico, effetto reale, OS supportati, prerequisiti, default auto-spunta sì/no, impatto, rischio, undo, esito test), codice, test, istruzioni, changelog e report benchmark.
- Default selezionati automaticamente per il PC reale; completa personalizzazione; rilevamento avvio giochi; Boost manuale e automatico; gestione sicura di Windows e terze parti; minima impronta runtime.
- Nessuna dichiarazione di “ripristino perfetto” se applicazioni chiuse non sono ricostruibili. Obiettivo verificabile: ripristino esatto di ogni stato modificato *che sia reversibile* e nessuna perdita di sessioni/documenti per gli interventi automatici.
- Non alterare altri progetti o cartelle. Lavorare nella cartella del progetto esistente e aggiungere questa specifica agli ordini precedenti quando il percorso viene identificato.

## Estensione richiesta — Auto-tuning GPU progressivo e reversibile
- Integrare, ove supportato dall'hardware e dai driver, una procedura **facoltativa** di calibrazione automatica della GPU, non un overclock universale o attivato senza consenso. Rilevare modello esatto, driver, frequenze stock/boost, memoria, limiti termici/energetici, sensori affidabili, capacità di regolazione e disponibilità di un'API supportata. Su GPU non compatibili o senza telemetria affidabile: sola diagnosi, nessuna modifica.
- Prima di ogni test conservare e rendere ripristinabili frequenze, curve, power target e ogni altro parametro effettivamente gestito; usare un watchdog indipendente, rollback su errore/crash e scadenza di sicurezza. Su instabilità, perdita telemetria o assenza di restore verificabile: arrestare la procedura e ripristinare i valori precedenti, senza riprovare automaticamente il profilo instabile.
- Cercare il compromesso fra FPS/frametime, stabilità, temperatura, rumorosità e consumi tramite piccoli incrementi, benchmark ripetuti e step di conferma; interrompersi prima dei limiti termici/energetici e ridurre il margine dopo eventuali errori. Non aumentare automaticamente tensione né aggirare limiti del produttore/BIOS/driver. Nessun test può garantire rischio zero: prevedere consenso informato, limiti conservativi, possibilità di annullare e default disattivato.
- Valutare separatamente clock core e memoria, evitando di interpretare FPS superiori ma instabili, throttling, artefatti o errori come un guadagno. Non effettuare stress test incontrollati su GPU vecchie, raffreddamento insufficiente o sensori assenti. Registrare condizioni e risultati dei test, comprese regressioni.
- Salvare solo un profilo che superi criteri di stabilità e beneficio ripetibile e che sia applicabile con sicurezza al gioco e alla macchina specifici. All'**Attiva Boost** il profilo può essere applicato soltanto dopo opt-in; alla **Disattiva Boost / Ripristina PC** ripristinare le impostazioni GPU precedenti anche con gioco aperto. In mancanza di restore affidabile, non abilitare l'auto-tuning.
- Aggiungere nella UI una sezione GPU con rilevamento hardware, profilo stock, profilo testato, stato test, dati benchmark, limiti, spiegazioni e comandi **Analizza / Calibra con consenso / Interrompi / Ripristina**. Completare test reali su hardware supportato prima di definirla pronta; l'implementazione e la stabilità non sono ancora verificate.
