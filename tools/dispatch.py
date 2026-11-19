"""Minimal Claude Code / Codex role dispatcher. Preview by default."""
import argparse
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ROLES = ("architect", "auditor", "implementer", "tester")
PATTERNS = (
    ("tester", r"\b(test|tests|testing|verific|collaud|regressioni)\w*\b"),
    ("auditor", r"\b(bug|errore|debug|diagnos|analizz|causa|fix)\w*\b"),
    ("implementer", r"\b(implement|scriv|crea|aggiung|modific|codic|corregg)\w*\b"),
)
CODEX_PROMPT = {
    "architect": "Definisci componenti/interfacce minimi; non modificare file.",
    "auditor": "Individua causa e prove; non modificare file.",
    "implementer": "Implementa solo la modifica richiesta e verifica con test mirati.",
    "tester": "Esegui solo test pertinenti e riporta risultati verificabili.",
}


def route(task, role="auto", provider="claude", level="normal", model=None):
    if not task.strip():
        raise ValueError("task vuoto")
    if role not in (*ROLES, "auto") or provider not in ("claude", "codex"):
        raise ValueError("ruolo o provider non valido")
    if level not in ("normal", "high"):
        raise ValueError("livello non valido")
    if role == "auto":
        role = next((r for r, pattern in PATTERNS if re.search(pattern, task, re.I)), "architect")
    if provider == "claude":
        selected_model = model or ("opus" if level == "high" else "sonnet")
        command = ["claude", "-p", "--agent", role, "--model", selected_model, task]
    else:
        # Codex does not load .claude/agents; pass a short role instruction.
        prompt = f"Ruolo: {role}. {CODEX_PROMPT[role]} Segui AGENTS.md. Compito: {task}"
        sandbox = "workspace-write" if role == "implementer" else "read-only"
        command = ["codex", "exec", "--sandbox", sandbox]
        if model:
            command.extend(["--model", model])
        if level == "high":
            command.extend(["--config", "model_reasoning_effort=high"])
        command.append(prompt)
        selected_model = model or "predefinito (configurazione Codex)"
    return {"status": "planned", "role": role, "provider": provider, "model": selected_model, "command": command}


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("task")
    p.add_argument("--role", choices=("auto", *ROLES), default="auto")
    p.add_argument("--provider", choices=("claude", "codex"), default="claude")
    p.add_argument("--level", choices=("normal", "high"), default="normal")
    p.add_argument("--model", help="Modello esplicito disponibile nel client")
    p.add_argument("--run", action="store_true", help="Esegui solo su richiesta esplicita")
    args = p.parse_args(argv)
    try:
        plan = route(args.task, args.role, args.provider, args.level, args.model)
    except ValueError as e:
        p.error(str(e))
    if not args.run:
        print(json.dumps(plan, ensure_ascii=False))
        return 0
    if plan["role"] == "implementer":
        branch = subprocess.run(["git", "symbolic-ref", "--short", "HEAD"],
                                cwd=ROOT, capture_output=True, text=True, check=False)
        if branch.returncode or branch.stdout.strip() in {"main", "master"}:
            print(json.dumps({"status": "blocked", "reason": "serve branch di lavoro"}))
            return 2
    if not shutil.which(plan["command"][0]):
        print(json.dumps({"status": "blocked", "reason": "client non trovato"}))
        return 2
    try:
        result = subprocess.run(plan["command"], cwd=ROOT, timeout=1800, check=False)
    except subprocess.TimeoutExpired:
        print(json.dumps({"status": "blocked", "reason": "timeout"}))
        return 2
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
