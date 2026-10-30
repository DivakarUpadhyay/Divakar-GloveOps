@echo off
REM Rebuilds the DivakarGloveOps database (schema, procedures, demo data, Power BI star schema)
cd /d "%~dp0"
sqlcmd -S DIVAKARLAPTOP\DEVELOPERMSSQL -E -b -i database\00_run_all.sql
