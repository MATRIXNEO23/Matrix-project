"""Lightweight, read-only project index for Matrix. Python standard library only."""
import argparse
import json
import sqlite3
from pathlib import Path

def build(root: Path, db: Path) -> int:
    root = root.resolve()
    projects = root / "progetti"
    db.parent.mkdir(parents=True, exist_ok=True)
    with sqlite3.connect(db) as con:
        con.execute("CREATE VIRTUAL TABLE IF NOT EXISTS files USING fts5(project, path UNINDEXED, content)")
        con.execute("DELETE FROM files")
        if not projects.is_dir():
            return 0
        count = 0
        for project in sorted(projects.iterdir()):
            if not project.is_dir() or project.is_symlink():
                continue
            for path in sorted(project.rglob("*")):
                if path.is_symlink() or not path.is_file() or path.stat().st_size > 200_000:
                    continue
                if any(p in {".git", ".venv", "node_modules", "__pycache__"} for p in path.parts):
                    continue
                if path.suffix.lower() not in {".py", ".md", ".txt", ".toml", ".json", ".yaml", ".yml", ".rs"}:
                    continue
                try:
                    content = path.read_text(encoding="utf-8")
                except (UnicodeError, OSError):
                    continue
                con.execute("INSERT INTO files(project,path,content) VALUES(?,?,?)",
                            (project.name, str(path.relative_to(root)), content))
                count += 1
    return count

def search(db: Path, query: str, project: str | None, limit: int = 10):
    with sqlite3.connect(db) as con:
        sql = "SELECT project,path,snippet(files,2,'[',']','...',12) FROM files WHERE files MATCH ?"
        params = [query]
        if project:
            sql += " AND project = ?"
            params.append(project)
        sql += " ORDER BY rank LIMIT ?"
        params.append(limit)
        return [dict(zip(("project", "path", "excerpt"), row)) for row in con.execute(sql, params)]

def main():
    parser = argparse.ArgumentParser(description="Matrix Librarian: local project search")
    parser.add_argument("action", choices=["index", "search"])
    parser.add_argument("query", nargs="?")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--project")
    args = parser.parse_args()
    root = args.root.resolve()
    db = root / ".matrix-cache" / "librarian.sqlite3"
    if args.action == "index":
        print(json.dumps({"indexed_files": build(root, db)}, ensure_ascii=False))
    else:
        if not args.query:
            parser.error("search requires a query")
        if not db.exists():
            parser.error("index not found: run index first")
        print(json.dumps(search(db, args.query, args.project), ensure_ascii=False, indent=2))

if __name__ == "__main__":
    main()
