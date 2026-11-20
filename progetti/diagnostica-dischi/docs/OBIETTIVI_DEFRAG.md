# Obiettivi deframmentatore HDD

## Priorità
Realizzare un deframmentatore per HDD meccanici rapido e intelligente, misurandone le prestazioni rispetto alle alternative. Non promettere superiorità prima dei benchmark.

## Funzioni richieste
- Analisi della frammentazione.
- Evitare spostamenti inutili e privilegiare il beneficio reale rispetto alla frammentazione zero.
- Raggruppamento dei file appartenenti alla stessa applicazione quando utile e tecnicamente possibile.
- Possibile raggruppamento dei file utilizzati insieme, se i dati necessari sono disponibili.
- Controllo preliminare dello stato del disco; in presenza di segnali di guasto dare priorità al backup e non avviare operazioni pesanti.
- SSD gestiti separatamente: niente deframmentazione HDD indiscriminata.
- Rilevamento e segnalazione dei cluster difettosi; riparazioni solo in una fase successiva e previa conferma.

## Vincoli
Prima fase di analisi in sola lettura. Nessuno spostamento dati su dischi reali finché non siano disponibili test e misure adeguate. Progetto indipendente, eventualmente integrabile in LUMI in futuro. Python per ora; Rust rinviato.

## Prossima attività
Implementare il primo rilevamento e la raccolta dati Windows in sola lettura, con test.
