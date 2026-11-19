"""Validate agent reports without confusing reported evidence with verified facts."""
import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STATUSES = {"done", "blocked"}
TEST_STATUSES = {"pass", "fail", "not_run"}


def validate(report, root=ROOT):
    errors = []
    if not isinstance(report, dict):
        return ["report deve essere un oggetto JSON"]
    if report.get("status") not in STATUSES:
        errors.append("status: done|blocked richiesto")
    for key in ("files", "tests", "evidence", "risks"):
        if not isinstance(report.get(key), list):
            errors.append(f"{key}: lista richiesta")
    if not isinstance(report.get("next"), str):
        errors.append("next: stringa richiesta")
    for item in report.get("files", []) if isinstance(report.get("files"), list) else []:
        if not isinstance(item, str) or not item or Path(item).is_absolute():
            errors.append("files: percorso relativo non valido")
            continue
        resolved = (root / item).resolve()
        if not resolved.is_relative_to(root.resolve()) or not resolved.is_file():
            errors.append(f"files: non trovato o fuori repo: {item}")
    for item in report.get("tests", []) if isinstance(report.get("tests"), list) else []:
        if not isinstance(item, dict) or not isinstance(item.get("name"), str) or item.get("result") not in TEST_STATUSES:
            errors.append("tests: ogni test richiede name e result: pass|fail|not_run")
    for key in ("evidence", "risks"):
        if isinstance(report.get(key), list) and not all(isinstance(x, str) for x in report[key]):
            errors.append(f"{key}: solo stringhe")
    if report.get("status") == "done":
        if not report.get("evidence"):
            errors.append("done richiede almeno un'evidenza dichiarata")
        if any(t.get("result") != "pass" for t in report.get("tests", []) if isinstance(t, dict)):
            errors.append("done incompatibile con test falliti o non eseguiti")
    return errors


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("report", help="Percorso del report JSON")
    args = p.parse_args(argv)
    try:
        with open(args.report, encoding="utf-8") as f:
            report = json.load(f)
    except (OSError, ValueError) as e:
        print(json.dumps({"valid": False, "errors": [str(e)]}, ensure_ascii=False))
        return 2
    errors = validate(report)
    print(json.dumps({"valid": not errors, "errors": errors}, ensure_ascii=False))
    return 2 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
