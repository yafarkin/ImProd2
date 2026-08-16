@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

rem ==================================================================
rem  LLM-боты im_prod — стадия 1 (один сектор), автономный прогон.
rem  Положите этот файл в корень репозитория (рядом с ImProd.sln) и
rem  запустите двойным щелчком. Настройте параметры ниже под себя —
rem  адрес сервера LM Studio уже подставлен под стационарный ПК.
rem
rem  На сколько это реально: ход одного бота на этом железе (частичный
rem  оффлоад на CPU/96ГБ ОЗУ) занимал 2-5 минут в замерах 2026-08-16.
rem  3 бота х 90 ходов — это часы, не минуты; окно можно свернуть и
rem  вернуться позже, прогон не остановится сам по себе, если только
rem  явно не застрянет (см. LLM_BOT_MAX_CONSECUTIVE_FAILURES ниже).
rem ==================================================================

set LM_STUDIO_BASE_URL=http://192.168.0.2:1234/v1/
set LLM_BOT_MODEL=qwen/qwen3.8-27b
set LLM_BOT_COUNT=3
set LLM_BOT_TURNS=90

rem Щедрый таймаут на один запрос — ловит настоящее зависание, не режет
rem честное долгое размышление модели.
set LLM_BOT_TIMEOUT_MINUTES=20

rem Попыток на один ход (сетевые сбои/битый JSON не должны срывать ход).
set LLM_BOT_MAX_ATTEMPTS=6

rem После скольких провалов подряд у ОДНОГО бота прогон останавливается
rem целиком — незачем гнать 90 ходов, если бэкенд явно сломался.
set LLM_BOT_MAX_CONSECUTIVE_FAILURES=8

set LLM_BOT_TEMPERATURE=0.4
set LLM_BOT_MAX_TOKENS=3000

rem ------------------------------------------------------------------

cd /d "%~dp0"

if not exist "ImProd.sln" (
    echo.
    echo ОШИБКА: этот .bat должен лежать в корне репозитория, рядом с ImProd.sln.
    echo Сейчас текущая папка: %cd%
    echo.
    pause
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo ОШИБКА: не найден dotnet. Установите .NET SDK 8 и запустите файл заново.
    echo.
    pause
    exit /b 1
)

echo === Проверка LM Studio по адресу %LM_STUDIO_BASE_URL% ===
powershell -NoProfile -Command "try { Invoke-RestMethod -Uri '%LM_STUDIO_BASE_URL%models' -TimeoutSec 10 | Out-Null; exit 0 } catch { exit 1 }"
if errorlevel 1 (
    echo.
    echo ОШИБКА: LM Studio не отвечает по адресу %LM_STUDIO_BASE_URL%
    echo Проверьте, что сервер запущен, модель загружена, адрес верный ^(это тот же
    echo хост:порт, что открыт в самом LM Studio^), и запустите файл заново.
    echo.
    pause
    exit /b 1
)
echo LM Studio отвечает, ОК.
echo.

echo === Сборка ===
dotnet build src\Game.Bots.Llm.Console\Game.Bots.Llm.Console.csproj -c Release
if errorlevel 1 (
    echo.
    echo ОШИБКА: сборка не удалась, см. вывод выше.
    echo.
    pause
    exit /b 1
)

echo.
echo === Запуск (модель: %LLM_BOT_MODEL%, ботов: %LLM_BOT_COUNT%, ходов: %LLM_BOT_TURNS%) ===
echo.
dotnet run --project src\Game.Bots.Llm.Console\Game.Bots.Llm.Console.csproj -c Release --no-build

echo.
echo Скрипт завершён. Файлы результатов ^(лог, метрики, сырой лог решений^) —
echo рядом с исполняемым файлом, пути к ним были показаны выше в самом начале вывода.
echo.
pause
