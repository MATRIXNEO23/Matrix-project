# Specifica iniziale dell’ottimizzatore RAM per Windows

## Riconoscimento e raggruppamento

L’app mostra i processi attivi e propone il raggruppamento per applicazione usando il percorso completo dell’eseguibile, i metadati del file e la relazione padre–figlio. Ogni gruppo è espandibile, così l’utente può vedere quali processi contiene. Quando i dati sono insufficienti o contraddittori, il processo resta separato con la dicitura «App da identificare».

## Misurazione della RAM

Per ogni processo, l’app mostra il *working set* privato, cioè la memoria privata attualmente residente in RAM. Il valore per applicazione è la somma dei valori dei suoi processi. Viene presentato come stima della RAM attribuibile al gruppo, perché la memoria condivisa resta fuori dal conteggio. Se mancano dati per alcuni processi, il totale viene indicato come parziale.

## Suggerimenti e chiusura automatica

Quando l’utilizzo della RAM supera la soglia configurata dall’utente, l’app ordina i suggerimenti per RAM privata residente. I processi essenziali, quelli protetti e quelli di identità incerta sono esclusi dalle azioni automatiche.

L’utente può autorizzare esplicitamente singole applicazioni. Prima di agire, l’ottimizzatore verifica di nuovo il percorso dell’eseguibile, il PID e l’ora di avvio del processo. Il raggruppamento suggerito serve alla visualizzazione. L’autorizzazione si applica solo alle identità approvate esplicitamente.

Per proteggere il lavoro non salvato, la chiusura automatica è disponibile solo quando l’applicazione offre un meccanismo verificabile di chiusura sicura. Se tale garanzia manca o l’app richiede una conferma, l’ottimizzatore demanda la decisione all’utente. La terminazione forzata è esclusa.

## Verifiche di accettazione

- Due eseguibili con lo stesso nome in percorsi diversi ricevono autorizzazioni distinte.
- Un’app avviata da un’altra non eredita il permesso di chiusura.
- Un processo riavviato o un PID riutilizzato richiede una nuova verifica prima di qualsiasi azione.
- Un documento con modifiche non salvate resta aperto quando l’ottimizzatore non può garantire una chiusura sicura.
- Il superamento della soglia produce suggerimenti anche se l’utente non ha autorizzato alcuna applicazione.
