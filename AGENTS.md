# Regole comuni — Matrix-project

- Esegui solo la richiesta esplicita. Nessuna funzione, dipendenza, astrazione o riga superflua.
- Prima di cambiare, leggi solo i file pertinenti; rispetta interfacce e decisioni esistenti.
- Non inventare file, API, misure, esiti, test o accessi. Se manca una prova, scrivi "non verificato".
- Per bug: riproduci quando possibile, identifica causa, proponi fix minimo, esegui test mirati.
- Non cambiare codice fuori dal progetto/cartella assegnata. Non toccare altri progetti.
- Non modificare direttamente il branch main: lavora su branch dedicato e apri PR.
- Chiedi chiarimenti solo per blocchi reali o azioni distruttive; altrimenti applica la scelta minima reversibile.
- Non distruggere dati, profili, backup o build esistenti. Per operazioni disco: analisi in sola lettura prima, conferma esplicita prima di scrivere.
- Output breve: FATTO, PROVE, RISCHI/BLOCCHI, PROSSIMO. Niente spiegoni o log grezzi salvo richiesta.
- Usa script e test deterministici prima di chiamare IA. Niente subagenti se il task è semplice.
- Subagenti: architetto, auditor, programmatore, tester. Separare diagnosi, implementazione e verifica.
- Modelli: Claude Sonnet per routine; Opus solo per compiti davvero difficili. Codex: minimo modello/sforzo disponibile adeguato. La scelta effettiva dipende dagli strumenti disponibili.
- Le skill descrivono il metodo, non eseguono lavoro autonomo. Attivare solo quelle pertinenti.
- Registra decisioni e mappe minime nel README/docs del progetto; non ripetere contesti lunghi.
