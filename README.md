# Matrix-project

Repository multiprogetto. La root contiene **solo il metodo di lavoro condiviso**.

- [AGENTS.md](AGENTS.md): regole comuni per gli agenti.
- [CLAUDE.md](CLAUDE.md): istruzioni Claude Code.
- [Workflow](docs/WORKFLOW.md): ruoli, passaggi, test, verifiche.
- [Architettura agenti](docs/ARCHITECTURE.md): componenti e comunicazione.
- [Skill](.agents/skills/): metodi leggeri; copie compatibili con Claude Code in `.claude/skills/`.
- [Subagenti Claude](.claude/agents/): architetto, auditor, implementer, tester.
- [Strumenti condivisi](tools/README.md): dispatcher, validatore, configurazione REA.

## Metodo operativo

`tools/dispatch.py` instrada e avvia i CLI solo con `--run` esplicito; `tools/validate.py` controlla i rapporti. REA resta esterno e richiede attivazione sul PC. Test: `python -m unittest discover -s tests -p 'test_*.py'`.

## Progetti software

Nessun progetto ancora avviato in questa repository. Ogni nuovo programma avrà una cartella propria, con codice, test, documentazione e README. L'elenco sarà aggiornato quando un progetto verrà creato.

Sviluppo su branch dedicati; la root condivisa va in `main` soltanto dopo verifica.
