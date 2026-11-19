# Architettura di lavoro (proposta)

```mermaid
flowchart TD
 U[Richiesta] --> O[Orchestratore Claude o Codex]
 O --> D[Dispatcher Python - previsto]
 D --> A[Architetto]
 D --> B[Auditor]
 D --> C[Programmatore]
 D --> T[Tester]
 A --> V[Validatore Python - previsto]
 B --> V
 C --> V
 T --> V
 V --> O
```

REA: analisi di software e binari con evidenze, quando pertinente. GrillMe: domande in progettazione e fix non banali. I diagrammi dei moduli reali stanno in `<progetto>/docs/`, non qui. Configurazioni e script Python non ancora realizzati.