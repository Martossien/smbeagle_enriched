#!/usr/bin/env python3
"""Relit le contrat CSV avec le parseur docia (mode strict, base temporaire).

À lancer avec l'interpréteur du venv docia. Vérifie :
- le CSV d'or tests/golden/scan_19col.csv ;
- un scan --local-path frais de tests/fixtures ;
- un scan d'un dossier temporaire avec guillemet, virgule, espaces et accents.
Chaque CSV doit s'importer sans aucune ligne invalide et sans écart de
guillemets sélectifs. Sort 0 si tout est conforme, 1 sinon.
"""

from __future__ import annotations

import os
import subprocess
import sys
import tempfile
from pathlib import Path

try:
    from docia.db import Database
    from docia.ingest.smbeagle_csv import HEADER, import_csv, validate_csv_line_format
except ImportError as exc:  # pragma: no cover - dépend de l'environnement
    print(f"docia introuvable dans cet interpréteur : {exc}")
    sys.exit(1)

ROOT = Path(__file__).resolve().parent.parent
METADATA = ["--sizefile", "--access-time", "--fileattributes", "--ownerfile", "--fasthash", "--file-signature"]


def find_executable() -> Path:
    name = "SMBeagle.exe" if os.name == "nt" else "SMBeagle"
    candidates = sorted(
        (ROOT / "bin" / "Release").rglob(name), key=lambda p: p.stat().st_mtime, reverse=True
    )
    if not candidates:
        raise SystemExit("exécutable introuvable : lancer dotnet build -c Release")
    return candidates[0]


def scan(exe: Path, target: Path, csv: Path) -> None:
    subprocess.run([str(exe), "--local-path", str(target), "-c", str(csv), "-q", *METADATA], check=True, capture_output=True)


def check_csv(label: str, csv: Path, expected_rows: int) -> list[str]:
    problems: list[str] = []
    with tempfile.TemporaryDirectory() as tmp:
        with Database(Path(tmp) / "docia.sqlite") as db:
            report = import_csv(db, csv, strict=True)
    if report.invalid:
        problems.append(f"{label} : {report.invalid} ligne(s) invalide(s) : {report.errors[:3]}")
    if report.total != expected_rows:
        problems.append(f"{label} : {report.total} lignes importées au lieu de {expected_rows}")
    lines = csv.read_text(encoding="utf-8-sig").splitlines()
    for number, line in enumerate(lines[1:], start=2):
        if line.strip():
            problems.extend(f"{label} : {e}" for e in validate_csv_line_format(line, number))
    print(f"{label} : {report.total} lignes, {report.invalid} invalide(s), {len(lines) - 1} lignes brutes")
    return problems


def main() -> int:
    assert len(HEADER) == 19
    exe = find_executable()
    problems: list[str] = []
    problems += check_csv("CSV d'or", ROOT / "tests" / "golden" / "scan_19col.csv", 7)
    with tempfile.TemporaryDirectory() as tmp:
        fresh = Path(tmp) / "fixtures.csv"
        scan(exe, ROOT / "tests" / "fixtures", fresh)
        problems += check_csv("scan des fixtures", fresh, 7)

        special = Path(tmp) / "Dossier Été, archivé"
        special.mkdir()
        names = ["résumé (v2).txt", "budget, prévisionnel.csv"]
        if os.name != "nt":
            names.append('rapport "final".txt')
        for name in names:
            (special / name).write_text("contenu", encoding="utf-8")
        csv = Path(tmp) / "special.csv"
        scan(exe, special, csv)
        problems += check_csv("noms spéciaux", csv, len(names))
        from docia.ingest.smbeagle_csv import read_smbeagle_csv

        got = sorted(row.name for row in read_smbeagle_csv(csv) if hasattr(row, "name"))
        if got != sorted(names):
            problems.append(f"noms relus {got} au lieu de {sorted(names)}")
    for p in problems:
        print("PROBLEME :", p)
    print("contrat docia :", "OK" if not problems else "ECHEC")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
