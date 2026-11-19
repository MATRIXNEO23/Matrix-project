# Metodo condiviso

```mermaid
flowchart TD
 U[Richiesta] --> O[Orchestratore Claude/Codex]
 O --> D[Dispatcher Python]
 D --> A[Architect]
 D --> B[Auditor]
 D --> C[Implementer]
 D --> T[Tester]
 A --> V[Validator Python]
 B --> V
 C --> V
 T --> V
 V --> O
 O -. MCP opzionale .-> R[REA locale]
```

Il dispatcher avvia CLI solo con `--run`; non è un server sempre acceso. Il validatore controlla report, non attesta che le prove dichiarate siano autentiche. REA richiede installazione/configurazione sul PC prima di risultare operativo.
