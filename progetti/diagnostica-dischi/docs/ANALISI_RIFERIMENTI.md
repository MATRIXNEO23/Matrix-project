# Programmi di riferimento — prima analisi

## 1. smartmontools (priorità tecnica)
- Repository ufficiale: https://github.com/smartmontools/smartmontools
- Licenza GPL-2.0; verificare obblighi prima di riutilizzare codice.
- Include smartctl e smartd; supporta Windows e dischi ATA/SATA, SCSI/SAS, NVMe.
- Studiare interfacce Windows, interpretazione SMART e formato JSON.
- Per capire la logica preferire sorgenti e documentazione; REA/Ghidra solo dove serve per analisi binaria.

## 2. Hard Disk Sentinel (riferimento funzionale)
- Sito ufficiale: https://www.hdsentinel.com/
- Commerciale, versione di prova disponibile.
- Studiare comportamento osservabile: stato salute, temperature, storico, allarmi, gestione dati mancanti.
- Nessuna copia di codice proprietario, nessun aggiramento licenze/protezioni.

## Procedura
1. Catalogare funzioni e limiti delle due applicazioni.
2. Eseguire eventuali analisi binarie con REA/Ghidra in ambiente autorizzato e isolato.
3. Scrivere specifiche indipendenti e implementare codice originale.
4. Verificare con dati di prova e controlli di regressione.
5. Prima fase solo lettura: nessuna scrittura o riparazione sui dischi.

## Stato
Ricerca documentale iniziale. Nessun binario analizzato con REA/Ghidra, nessuna prova locale eseguita.
