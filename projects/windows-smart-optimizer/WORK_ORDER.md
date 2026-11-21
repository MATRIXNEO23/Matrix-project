# Ordine di lavoro — Windows Smart Optimizer
Data: 2026-11-21
Stato: specifica di incarico; nessuna implementazione o benchmark attestati.

## Mandato
Progettare, implementare, compilare e verificare un ottimizzatore Windows professionale, leggero e attraente, destinato **prima di tutto ai PC datati con 4 GB di RAM** e compatibile, ove verificato, anche con PC moderni. Non consegnare soltanto un elenco di processi o una copia di Gestione attività: l'obiettivo è l'**ottimizzazione automatica, misurabile e reversibile**, soprattutto durante i giochi.

## Funzioni richieste
1. **Monitoraggio reale a basso overhead**: RAM disponibile e impegnata, working set e memoria privata per processo, CPU, disco, processi/servizi; campionamento adattivo per non appesantire macchine vecchie. Distinzione chiara fra memoria occupata, cache utile e reale pressione di memoria.
2. **Classificazione processi e dipendenze**: sistema critico/protetto, gioco e dipendenze, applicazione utente, attività opzionale. Nessuna lista universale di servizi "inutili": le decisioni dipendono da configurazione, dipendenze e misure sul PC.
3. **Motore di policy**: profili Conservativo, Bilanciato, Aggressivo, ciascuna azione attivabile/disattivabile manualmente e corredata di motivazione, prerequisiti, rischio, permessi e rollback.
4. **Game Booster**: riconoscimento avvio/chiusura dei giochi; snapshot iniziale; eventuale rinvio di attività secondarie e gestione autorizzata di processi/servizi non essenziali; monitoraggio durante la sessione; ripristino alla chiusura, in caso di crash, e al riavvio dopo interruzioni.
5. **Ottimizzazione RAM onesta**: non forzare indiscriminatamente trim dei working set o svuotamento cache; controllare anche hard faults, paging, latenza, stabilità e fluidità/FPS dove misurabili. Non promettere RAM recuperata o FPS aggiuntivi senza prove.
6. **Adattamento locale**: registrare intervento, condizioni, risultato, stabilità e rollback; privilegiare solo interventi con beneficio ripetibile e disabilitare quelli inefficaci o dannosi. Nessuna IA pesante o cloud necessario.
7. **UI professionale**: dashboard moderna e leggibile, preferibilmente dark; andamento risorse, stato booster, profili, azioni proposte/eseguite, confronto prima/dopo, pulsante ottimizzazione e ripristino. Estetica curata con grafica minimale e consumo ridotto.
8. **Distribuzione**: preferire C++ nativo e API documentate di Windows; evitare runtime pesanti. Valutare versione portable e privilegi elevati per singola azione solo quando indispensabili; nessuna modifica persistente nascosta.

## Sicurezza e reversibilità
- Vietato terminare/sospendere processi critici, driver, antivirus, servizi di sicurezza, audio/rete necessari, o servizi sconosciuti senza valutazione specifica.
- Ogni modifica deve essere registrata *prima* di essere applicata, con stato originale e modalità di ripristino. Usare whitelist/denylist motivata, dry-run e opt-in per azioni rischiose.
- Non disattivare permanentemente Windows Update, Defender o meccanismi di protezione. Non modificare impostazioni irreversibili per ottenere numeri migliori in dashboard.
- Prima dei test reali che alterano processi/servizi, predisporre ambiente di prova e meccanismo di recupero; richiedere autorizzazione per azioni distruttive o invasive.

## Metodo di esecuzione autonoma
- Leggere prima le regole della repository (`AGENTS.md`, `CLAUDE.md`, `docs/WORKFLOW.md` dove pertinenti). Operare **solo** nella cartella del progetto, su branch dedicato e PR: non modificare `main` direttamente.
- Inventariare strumenti e codice realmente disponibili; distinguere accesso confermato, ipotesi e blocchi. Se una specifica tecnica del sistema bersaglio manca, formulare un'ipotesi verificabile e testarla.
- Produrre una mappa minima dei moduli, delle interfacce, delle dipendenze e dei criteri di accettazione; quindi **iniziare l'implementazione**, senza fermarsi a relazioni di analisi o chiedere all'utente di scegliere ogni micro-passaggio.
- Procedere verticalmente con incremento funzionante: telemetria → policy dry-run → prima azione sicura con rollback → rilevamento gioco → orchestrazione e ripristino → UI → misure adattive.
- Compilare, testare, correggere, ripetere. Conservare le scelte tecniche e i risultati di test nel README/docs del progetto; evitare output verbosi e ripetizioni inutili.
- Chiedere all'utente solo per decisioni di prodotto effettivamente irrisolte, credenziali/autorizzazioni mancanti, modifiche significative al perimetro e operazioni rischiose. Non demandare all'utente i comandi che l'agente può eseguire davvero con accessi autorizzati.

## Criteri di accettazione
- Eseguibile reale e installazione/avvio documentati.
- Letture monitoraggio confrontate con fonti attendibili Windows.
- Almeno un intervento di ottimizzazione effettivamente operativo, sicuro e con ripristino testato.
- Riconoscimento automatico di almeno un gioco di prova e corretto ritorno allo stato originale alla chiusura/crash.
- Nessun degrado noto di sicurezza, servizi essenziali o utilizzo normale.
- UI funzionante e utilizzabile anche su hardware poco potente.
- Misure prima/dopo ripetute in condizioni comparabili, con risultati anche negativi esplicitati.
- Test automatizzati pertinenti e test Windows reali (anche PC vecchi/4 GB o hardware rappresentativo) documentati; senza macchina disponibile, indicare "non verificato" anziché simulare il collaudo.

## Consegna
Ogni rapporto sintetico: **FATTO** (solo risultati realmente ottenuti), **PROVE** (commit, build, log e misure), **RISCHI/BLOCCHI**, **PROSSIMO**. Non dichiarare completato il software se manca anche uno solo dei criteri essenziali; distinguere chiaramente un prototipo parziale da un prodotto finito.

**Principio guida:** lo scopo dell'agente è costruire e collaudare il programma, non trasformare l'utente nell'esecutore di una successione infinita di script PowerShell.
