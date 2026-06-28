@echo off
REM ============================================================
REM  VTuber My Avatar - start the webcam tracker
REM  Run setup_tracker.bat first (one time). Then double-click
REM  this to begin streaming tracking data to the Unity app.
REM  Any arguments are passed through, e.g.:
REM      start_tracker.bat --camera 1
REM      start_tracker.bat --list-cameras
REM ============================================================
setlocal
cd /d "%~dp0"

if not exist ".venv\Scripts\activate.bat" (
    echo [run] No virtual environment found. Run setup_tracker.bat first.
    pause
    exit /b 1
)

call ".venv\Scripts\activate.bat"
python face_tracker.py %*

REM Keep window open if it exited with an error so you can read it.
if errorlevel 1 pause
