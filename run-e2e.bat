@echo off
REM Divakar GloveOps - end-to-end test in a visible Chrome window (resets the demo database first).
REM Don't use the mouse/keyboard in the test Chrome window while it runs. Screenshots: docs\screenshots
cd /d "%~dp0"
set GLOVEOPS_E2E=1
dotnet test tests\Divakar.GloveOps.E2E --logger "console;verbosity=normal"
