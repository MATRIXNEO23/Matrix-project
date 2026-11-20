# Librarian — indice condiviso

Quinto ruolo Matrix: ricerca locale leggera, senza API IA. Codex e Claude Code usano lo stesso comando Python e lo stesso database SQLite FTS5 nella medesima copia della repository.

```sh
python tools/librarian.py index
python tools/librarian.py search SMART --project diagnostica-dischi
```

Indicizza file testuali piccoli sotto `progetti/` e restituisce percorsi e frammenti. Il database generato in `.matrix-cache/librarian.sqlite3` è ricostruibile e non va committato. Le decisioni durevoli restano nella documentazione del singolo progetto. Librarian non modifica il codice dei progetti.

Claude Code: `.claude/agents/librarian.md`. Codex: `AGENTS.md` e CLI condivisa. Il collegamento MCP, l'aggiornamento automatico e i test nei due client non sono ancora verificati.
