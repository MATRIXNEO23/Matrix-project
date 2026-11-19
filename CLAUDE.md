# Claude Code

Segui [AGENTS.md](AGENTS.md). Non duplicarne le regole nel prompt.

- Per un progetto, leggi prima il suo README e solo i file rilevanti.
- Usa le skill in `.claude/skills/` quando servono.
- Sonnet per incarichi ordinari; Opus solo se la complessità lo giustifica. Non cambiare modello senza supporto nel runtime.
- Delegare soltanto task delimitati; il risultato torna all'orchestratore in forma sintetica.
- Nessuna modifica su `main`; nessun codice oltre lo scopo richiesto.
