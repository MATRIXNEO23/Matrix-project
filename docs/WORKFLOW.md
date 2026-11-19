# Metodo minimo

1. Orchestratore riceve il compito; usa script prima di subagenti se basta.
2. `python tools/dispatch.py "compito" --provider claude|codex`: anteprima JSON, nessuna chiamata IA.
3. `--role architect|auditor|implementer|tester` forza il ruolo; `--run` esegue il CLI solo su richiesta. Implementer bloccato su `main`.
4. Claude: Sonnet normale, Opus solo `--level high`. Codex: modello configurato dal client; `--level high` alza lo sforzo. Nessun modello aggiuntivo.
5. Subagente restituisce rapporto minimo JSON: `status`, `files`, `tests`, `evidence`, `risks`, `next`.
6. `python tools/validate.py report.json` controlla schema, percorsi e risultati dichiarati. Le prove restano da verificare: non certifica test non eseguiti.
7. Orchestratore verifica i test reali e propone integrazione; nessuna scrittura direttamente su `main` da agenti.

Test locali: `python -m unittest discover -s tests -p 'test_*.py'`.
REA: opzionale, solo per analisi che ne beneficiano; vedi `tools/README.md`.
