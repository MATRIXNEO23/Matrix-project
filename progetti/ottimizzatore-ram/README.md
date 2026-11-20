# Ottimizzatore RAM

## Obiettivo
Applicazione Windows per riconoscere i processi, collegarli alle rispettive applicazioni anche quando i nomi differiscono e gestire la memoria secondo regole autorizzate dall'utente.

## Funzioni concordate
- Riconoscere i processi e suggerire l'applicazione di appartenenza, anche quando i nomi sono diversi.
- Raggruppare i processi per applicazione e mostrare il consumo complessivo di RAM.
- Distinguere i processi essenziali di Windows dalle applicazioni che possono essere chiuse.
- Suggerire quali applicazioni potrebbero essere chiuse per recuperare memoria.
- Consentire all'utente di selezionare esplicitamente le applicazioni autorizzate alla chiusura.
- Consentire una soglia di utilizzo RAM configurabile; al superamento, intervenire automaticamente solo sulle applicazioni precedentemente autorizzate.
- Evitare chiusure che causino perdita di lavoro non salvato.

## Precisazione tecnica
Liberare la memoria di un'applicazione può richiedere la chiusura dei suoi processi; non è garantito che sia possibile scaricarla dalla RAM mantenendola attiva.

## Stato
Solo requisiti iniziali. Nessuna implementazione o test ancora eseguiti.
