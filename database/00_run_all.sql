/* Rebuilds the whole demo database.
   sqlcmd -S DIVAKARLAPTOP\DEVELOPERMSSQL -E -b -i database\00_run_all.sql
   (run from the repository root) */
:on error exit
:r database\01_schema.sql
:r database\02_procedures.sql
:r database\03_seed.sql
:r database\04_bi_star_schema.sql
