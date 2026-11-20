# Diagnostica dischi

Primo progetto software di Matrix. Applicazione autonoma per Windows, progettata per futura integrazione opzionale con LUMI, senza modificarlo ora.

## Obiettivo iniziale
Rilevare i dischi HDD/SSD presenti e mostrare informazioni disponibili su modello, capacità, stato di salute SMART, temperatura e usura, distinguendo sempre dati effettivamente disponibili da dati non supportati.

## Regole
- Fase 1 esclusivamente in lettura; nessuna riparazione, formattazione, cancellazione o modifica del disco.
- Non dichiarare un disco sano soltanto perché non si rilevano errori: segnalare limiti dei sensori e della lettura SMART.
- Tutti i file del programma, test e documenti restano in questa cartella.
- Il programma resta indipendente da LUMI; eventuale interfaccia di integrazione sarà definita dopo.
- Niente Rust per ora. Prima una base Python verificabile.

## Stato
Avvio del progetto: struttura e requisiti iniziali. Nessuna diagnostica implementata o verificata.

## Prossimo passo
Implementare rilevamento dischi Windows in sola lettura, con output strutturato e test mirati.
