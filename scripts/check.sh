#!/usr/bin/env bash
# Contrôle complet avant push : build Release sans aucun avertissement,
# format vérifié, tests xunit, contrat CSV relu par docia (si disponible).
# Affiche un verdict explicite en dernière ligne et sort en erreur au moindre échec.
#
# Variables optionnelles :
#   DOCIA_PYTHON  interpréteur Python du venv docia (défaut : chemin local du
#                 dépôt llm-content-analyzer) ; la vérification est sautée s'il
#                 est absent.
set -u
cd "$(dirname "$0")/.."
export DOTNET_CLI_UI_LANGUAGE=en DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
fail=0
status() { if [ "$1" -eq 0 ]; then echo "  -> OK"; else echo "  -> ECHEC"; fail=1; fi; }

echo "== build Release (-warnaserror)"
out=$(dotnet build SMBeagle.sln -c Release -warnaserror -v q 2>&1); rc=$?
[ $rc -eq 0 ] || echo "$out" | grep -E "error|warning" | sort -u | head -30
status $rc

echo "== dotnet format --verify-no-changes"
out=$(dotnet format SMBeagle.sln --verify-no-changes -v q 2>&1); rc=$?
[ $rc -eq 0 ] || echo "$out" | grep -E "error|warning" | head -30
status $rc

echo "== dotnet test"
out=$(dotnet test SMBeagle.sln -c Release --no-build -v q 2>&1); rc=$?
echo "$out" | grep -E "^(Passed|Failed|Total|Skipped)|Failed [A-Za-z]|error" | head -40
n=$(echo "$out" | grep -oE "Passed! +- Failed: +[0-9]+, Passed: +[0-9]+, Skipped: +[0-9]+, Total: +[0-9]+" \
    | grep -oE "Total: +[0-9]+" | grep -oE "[0-9]+" | paste -sd+ | bc 2>/dev/null)
status $rc

DOCIA_PYTHON=${DOCIA_PYTHON:-/home/admin_ia/Doc-IA/llm-content-analyzer/.venv/bin/python}
if [ -x "$DOCIA_PYTHON" ] && [ -f scripts/check_docia_contract.py ]; then
    echo "== contrat CSV relu par docia ($DOCIA_PYTHON)"
    "$DOCIA_PYTHON" scripts/check_docia_contract.py; rc=$?
    status $rc
else
    echo "== contrat CSV relu par docia : sauté (venv docia introuvable)"
fi

if [ $fail -eq 0 ]; then echo "VERDICT: OK (${n:-0} tests)"; else echo "VERDICT: ECHEC"; fi
exit $fail
