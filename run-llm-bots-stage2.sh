#!/usr/bin/env bash
set -euo pipefail

# ==================================================================
#  LLM bots for im_prod - stage 2 (two sectors: metallurgy A +
#  petrochemistry B), unattended run. Same runner as
#  run-llm-bots-stage1.sh, different production model + bot layout -
#  see LLM_BOT_PRODUCTION_MODEL/LLM_BOT_SECTORS below. Put this file
#  in the repository root (next to ImProd.sln) and run it:
#      chmod +x run-llm-bots-stage2.sh   # once
#      ./run-llm-bots-stage2.sh
#  Adjust the settings below for your setup - the LM Studio server
#  address is already set for the desktop PC.
#
#  Purpose (request 2026-08-20): first confirm a single stage-1 bot
#  behaves normally with the latest fixes (rate cap, wear warning,
#  required "reason", checkpoint/resume), THEN move to this script -
#  it puts one bot in sector A and one in sector B so the public
#  trade-offer board (PostSellOffer/PostBuyOffer/FulfillTradeOffer)
#  actually gets exercised between two different sectors, not just
#  built and left untested.
#
#  How long this really takes: one bot's turn on that hardware
#  (partial CPU/96GB RAM offload) took 2-5 minutes in the 2026-08-16
#  measurements. 2 bots x 90 turns can be many hours; the run won't
#  stop on its own unless it actually gets stuck (see
#  LLM_BOT_MAX_CONSECUTIVE_FAILURES below).
#
#  Interrupted a run (Ctrl+C, closed the terminal, machine slept)?
#  Just run this script again - it finds ".working.json" next to the
#  executable and continues from the last completed turn instead of
#  starting over. That file (and the session journal alongside it)
#  are deleted automatically once a run finishes or gives up cleanly;
#  don't touch them by hand while a run is in progress. Don't switch
#  between running stage1.sh and stage2.sh while a ".working.json"
#  from the other one is still sitting there unresolved - they share
#  the same executable folder and checkpoint file name.
# ==================================================================

export LM_STUDIO_BASE_URL="http://192.168.0.2:1234/v1/"
export LLM_BOT_MODEL="openai/gpt-oss-20b"

# Stage 2 production model (metallurgy A + petrochemistry B, see
# docs/production-staging.md) and one bot per sector, round-robin.
export LLM_BOT_PRODUCTION_MODEL="metallurgy-petrochemistry.json"
export LLM_BOT_SECTORS="A,B"
export LLM_BOT_COUNT=2
export LLM_BOT_TURNS=90

# Generous timeout for a single request - catches a real hang without
# cutting off honest long thinking by the model.
export LLM_BOT_TIMEOUT_MINUTES=20

# Retries per turn (network hiccups/malformed JSON shouldn't derail a turn).
export LLM_BOT_MAX_ATTEMPTS=6

# After this many consecutive failures for ONE bot, the whole run
# stops - no point grinding through 90 turns if the backend is
# clearly broken.
export LLM_BOT_MAX_CONSECUTIVE_FAILURES=8

# One LLM call decides the whole turn as a batch of actions (build,
# hire, raise R&D, ...) - this caps how many actions can be in that
# one batch; anything beyond it is dropped. Lowered from 8 (2026-08-16
# live runs: weak/small models reliably filled this cap repeating the
# same wasteful action instead of stopping on their own).
export LLM_BOT_MAX_ACTIONS_PER_TURN=5

export LLM_BOT_TEMPERATURE=0.4
export LLM_BOT_MAX_TOKENS=3000

# Model reasoning/"thinking" is OFF by default (saves tokens and time).
# Set to 0 below to re-enable it if your model needs it.
export LLM_BOT_DISABLE_THINKING=1

# ------------------------------------------------------------------

cd "$(dirname "$0")"

if [ ! -f "ImProd.sln" ]; then
    echo
    echo "ERROR: this script must live in the repository root, next to ImProd.sln."
    echo "Current folder: $(pwd)"
    echo
    read -r -p "Press Enter to exit..."
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo
    echo "ERROR: dotnet not found. Install .NET SDK 8 and run this script again."
    echo
    read -r -p "Press Enter to exit..."
    exit 1
fi

echo "=== Checking LM Studio at ${LM_STUDIO_BASE_URL} ==="
if ! curl -fsS -m 10 "${LM_STUDIO_BASE_URL}models" >/dev/null 2>&1; then
    echo
    echo "ERROR: LM Studio is not responding at ${LM_STUDIO_BASE_URL}"
    echo "Check that the server is running, the model is loaded, the address"
    echo "is correct (the same host:port shown in LM Studio itself), and"
    echo "run this script again."
    echo
    read -r -p "Press Enter to exit..."
    exit 1
fi
echo "LM Studio is responding, OK."
echo

echo "=== Build ==="
if ! dotnet build src/Game.Bots.Llm.Console/Game.Bots.Llm.Console.csproj -c Release; then
    echo
    echo "ERROR: build failed, see the output above."
    echo
    read -r -p "Press Enter to exit..."
    exit 1
fi

echo
echo "=== Run (model: ${LLM_BOT_MODEL}, production model: ${LLM_BOT_PRODUCTION_MODEL}, " \
     "sectors: ${LLM_BOT_SECTORS}, bots: ${LLM_BOT_COUNT}, turns: ${LLM_BOT_TURNS}) ==="
echo
dotnet run --project src/Game.Bots.Llm.Console/Game.Bots.Llm.Console.csproj -c Release --no-build

echo
echo "Script finished. Result files (log, metrics, raw decision log) are next"
echo "to the executable; their paths were printed above at the very start of"
echo "the output."
echo
read -r -p "Press Enter to exit..."
