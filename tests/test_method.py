import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from dispatch import route, main as dispatch_main
from validate import validate


class DispatcherTests(unittest.TestCase):
    def test_preview_claude(self):
        p = route("Progetta il componente")
        self.assertEqual(p["role"], "architect")
        self.assertEqual(p["model"], "sonnet")
        self.assertEqual(p["command"][:5], ["claude", "-p", "--agent", "architect", "--model"])

    def test_high_selects_opus(self):
        self.assertEqual(route("progetta", level="high")["model"], "opus")

    def test_bug_auditor_readonly(self):
        p = route("Analizza il bug", provider="codex")
        self.assertEqual(p["role"], "auditor")
        self.assertIn("read-only", p["command"])

    def test_implementer_writable(self):
        p = route("Implementa funzione", provider="codex")
        self.assertEqual(p["role"], "implementer")
        self.assertIn("workspace-write", p["command"])

    def test_explicit_role(self):
        self.assertEqual(route("controlla", role="tester")["role"], "tester")

    def test_missing_client_blocks(self):
        with patch("dispatch.shutil.which", return_value=None):
            self.assertEqual(dispatch_main(["test", "--run"]), 2)

    def test_implementer_blocks_main(self):
        from subprocess import CompletedProcess
        with patch("dispatch.subprocess.run", return_value=CompletedProcess([], 0, "main\n", "")):
            self.assertEqual(dispatch_main(["implementa", "--run"]), 2)

    def test_high_codex_effort(self):
        self.assertIn("model_reasoning_effort=high", route("progetta", provider="codex", level="high")["command"])

    def test_preview_never_runs(self):
        with patch("dispatch.subprocess.run", side_effect=AssertionError("executed")):
            self.assertEqual(dispatch_main(["implementa"]), 0)

    def test_task_empty(self):
        with self.assertRaises(ValueError):
            route("  ")


class ValidatorTests(unittest.TestCase):
    def setUp(self):
        self.good = {"status": "done", "files": ["tools/dispatch.py"],
                     "tests": [{"name": "unit", "result": "pass"}],
                     "evidence": ["Eseguito test"], "risks": [], "next": ""}

    def test_valid(self):
        self.assertEqual(validate(self.good), [])

    def test_file_outside(self):
        self.good["files"] = ["../outside.txt"]
        self.assertTrue(validate(self.good))

    def test_missing_file(self):
        self.good["files"] = ["tools/NOT_HERE.py"]
        self.assertTrue(validate(self.good))

    def test_failed_test(self):
        self.good["tests"][0]["result"] = "fail"
        self.assertTrue(validate(self.good))

    def test_bad_shape(self):
        self.assertTrue(validate([]))

    def test_no_evidence(self):
        self.good["evidence"] = []
        self.assertTrue(validate(self.good))


if __name__ == "__main__":
    unittest.main()
