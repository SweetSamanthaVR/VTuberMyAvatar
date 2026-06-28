@echo off
REM ============================================================
REM  VTuber My Avatar - tracker one-time setup
REM  Creates a local Python venv, installs dependencies, and
REM  downloads the MediaPipe face landmark model.
REM  Just double-click this file once.
REM ============================================================
setlocal
cd /d "%~dp0"

echo.
echo === VTuber My Avatar tracker setup ===
echo.

REM --- locate Python ---
REM Use `if errorlevel` (evaluated live) plus a goto rather than %ERRORLEVEL% nested inside
REM a parenthesized if/else: in a single block %ERRORLEVEL% is expanded once, at parse time,
REM so the nested check would read the stale value and wrongly report "not found" on
REM machines that have `python` but not the `py` launcher.
where py >nul 2>nul
if not errorlevel 1 (
    set "PY=py -3"
    goto :py_found
)
where python >nul 2>nul
if not errorlevel 1 (
    set "PY=python"
    goto :py_found
)
echo [setup] ERROR: Python was not found.
echo [setup] Install Python 3.10-3.12 from https://www.python.org/downloads/
echo [setup] and tick "Add python.exe to PATH" during install, then re-run this.
pause
exit /b 1
:py_found

echo [setup] Using Python: %PY%
%PY% --version

REM --- create venv ---
if not exist ".venv" (
    echo [setup] Creating virtual environment .venv ...
    %PY% -m venv .venv
    if errorlevel 1 ( echo [setup] ERROR creating venv & pause & exit /b 1 )
) else (
    echo [setup] Reusing existing .venv
)

call ".venv\Scripts\activate.bat"

echo [setup] Upgrading pip ...
python -m pip install --upgrade pip

echo [setup] Installing dependencies (this can take a few minutes) ...
python -m pip install -r requirements.txt
if errorlevel 1 ( echo [setup] ERROR installing dependencies & pause & exit /b 1 )

REM --- download the MediaPipe model ---
if not exist "models" mkdir "models"
if not exist "models\face_landmarker.task" (
    echo [setup] Downloading MediaPipe face_landmarker model ...
    powershell -NoProfile -Command "try { Invoke-WebRequest -Uri 'https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task' -OutFile 'models\face_landmarker.task' } catch { Write-Host $_; exit 1 }"
    if errorlevel 1 ( echo [setup] ERROR downloading model & pause & exit /b 1 )
) else (
    echo [setup] Model already present.
)

echo [setup] Verifying protocol ...
python protocol.py

echo.
echo === Setup complete ===
echo Next: run  start_tracker.bat   (and check your camera with --list-cameras if needed)
echo.
pause
