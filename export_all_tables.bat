@echo off
REM Export all registered *.xlsx from excel\. See MonsterCsvToJson ExportAllWorkbooks tableExporters.
REM Requires .NET 8 SDK. Keep this BAT in Booom202604 project root next to BOOOM202604.csproj.

setlocal
pushd "%~dp0"

dotnet run --project "Tools\MonsterCsvToJson\MonsterCsvToJson.csproj" -- export-all

if errorlevel 1 (
  echo.
  echo export_all_tables.bat FAILED
  popd
  exit /b 1
)

echo.
echo export_all_tables.bat OK
popd
exit /b 0
