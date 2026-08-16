@echo off
chcp 866 >nul
setlocal enabledelayedexpansion

rem ==================================================================
rem  LLM bots for im_prod - stage 1 (single sector), unattended run.
rem  Put this file in the repository root (next to ImProd.sln) and
rem  double-click it. Adjust the settings below for your setup - the
rem  LM Studio server address is already set for the desktop PC.
rem
rem  How long this really takes: one bot's turn on this hardware
rem  (partial CPU/96GB RAM offload) took 2-5 minutes in the 2026-08-16
rem  measurements. 3 bots x 90 turns is hours, not minutes; you can
rem  minimize the window and come back later - the run won't stop on
rem  its own unless it actually gets stuck (see
rem  LLM_BOT_MAX_CONSECUTIVE_FAILURES below).
rem ==================================================================

set LM_STUDIO_BASE_URL=http://192.168.0.2:1234/v1/
set LLM_BOT_MODEL=qwen/qwen3.8-27b
rem set LLM_BOT_MODEL=gemma-2-9b-it
set LLM_BOT_COUNT=3
set LLM_BOT_TURNS=90

rem Generous timeout for a single request - catches a real hang without
rem cutting off honest long thinking by the model.
set LLM_BOT_TIMEOUT_MINUTES=20

rem Retries per turn (network hiccups/malformed JSON shouldn't derail a turn).
set LLM_BOT_MAX_ATTEMPTS=6

rem After this many consecutive failures for ONE bot, the whole run
rem stops - no point grinding through 90 turns if the backend is
rem clearly broken.
set LLM_BOT_MAX_CONSECUTIVE_FAILURES=8

rem One LLM call decides the whole turn as a batch of actions (build,
rem hire, raise R&D, ...) - this caps how many actions can be in that
rem one batch; anything beyond it is dropped. Lowered from 8 (2026-08-16
rem live runs: weak/small models reliably filled this cap repeating the
rem same wasteful action instead of stopping on their own).
set LLM_BOT_MAX_ACTIONS_PER_TURN=5

set LLM_BOT_TEMPERATURE=0.4
set LLM_BOT_MAX_TOKENS=3000

rem Model reasoning/"thinking" is OFF by default (saves tokens and time).
rem Set to 0 below to re-enable it if your model needs it.
set LLM_BOT_DISABLE_THINKING=1

rem ------------------------------------------------------------------

cd /d "%~dp0"

if not exist "ImProd.sln" (
    echo.
    echo ERROR: this .bat must live in the repository root, next to ImProd.sln.
    echo Current folder: %cd%
    echo.
    pause
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo ERROR: dotnet not found. Install .NET SDK 8 and run this file again.
    echo.
    pause
    exit /b 1
)

echo === Checking LM Studio at %LM_STUDIO_BASE_URL% ===
powershell -NoProfile -Command "try { Invoke-RestMethod -Uri '%LM_STUDIO_BASE_URL%models' -TimeoutSec 10 | Out-Null; exit 0 } catch { exit 1 }"
if errorlevel 1 (
    echo.
    echo ERROR: LM Studio is not responding at %LM_STUDIO_BASE_URL%
    echo Check that the server is running, the model is loaded, the address
    echo is correct ^(the same host:port shown in LM Studio itself^), and
    echo run this file again.
    echo.
    pause
    exit /b 1
)
echo LM Studio is responding, OK.
echo.

echo === Build ===
dotnet build src\Game.Bots.Llm.Console\Game.Bots.Llm.Console.csproj -c Release
if errorlevel 1 (
    echo.
    echo ERROR: build failed, see the output above.
    echo.
    pause
    exit /b 1
)

echo.
echo === Run (model: %LLM_BOT_MODEL%, bots: %LLM_BOT_COUNT%, turns: %LLM_BOT_TURNS%) ===
echo.
dotnet run --project src\Game.Bots.Llm.Console\Game.Bots.Llm.Console.csproj -c Release --no-build

echo.
echo Script finished. Result files ^(log, metrics, raw decision log^) are next
echo to the executable; their paths were printed above at the very start of
echo the output.
echo.
pause
