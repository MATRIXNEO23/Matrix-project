# Strumenti del metodo

- `dispatch.py`: instrada un compito verso Claude Code o Codex; anteprima predefinita.
- `validate.py`: verifica formato, percorsi nella repo, esiti dichiarati.
- `setup-rea.ps1`: anteprima di configurazione REA (PowerShell); `-Apply` applica dopo conferma interattiva.

REA è software esterno: [morluto/rea](https://github.com/morluto/rea). La configurazione usa il pacchetto ufficiale `rea-agents@6.9.0` e registra MCP per `claude_code` e `codex` senza duplicare il codice né reinstallare Node. Il server sarà **operativo solo dopo** setup e verifica sul PC; non è verificabile dalla repository. Comando di controllo: `npx rea-agents@6.9.0 doctor --client claude_code --client codex --json` (solo dopo setup). Non usare REA per compiti ordinari.

Nessuna modifica a LUMI, altri progetti o configurazioni locali finché lo script non viene eseguito esplicitamente sul computer.
