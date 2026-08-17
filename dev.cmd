@echo off
:: dev.cmd — Run CRS Reporter for local development (Windows)
::
:: Starts the .NET server (via `dotnet watch`, auto-rebuilds on save) and the
:: Vite dev server for the Vue UI in separate windows. Close either window to
:: stop that process.
::
:: Usage:
::   dev.cmd
::
:: Requirements:
::   - Node.js + npm  (for the Vue UI)
::   - .NET 10 SDK    (for the C# server)
::
:: URLs:
::   UI (Vite, hot reload):  http://localhost:5173
::   Server (API/WebSocket): http://localhost:9876
::
:: The UI dev server talks to the backend via ui\.env (VITE_API_URL /
:: VITE_WS_URL), not a Vite proxy — no extra config needed as long as the
:: server runs on its default port 9876.
::
:: ─────────────────────────────────────────────────────────────────────────────

setlocal

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"

echo.
echo  CRS Reporter - Dev Environment
echo  ================================
echo.

if not exist "%SCRIPT_DIR%\ui\node_modules" (
    echo [setup] node_modules missing - running npm install first...
    pushd "%SCRIPT_DIR%\ui"
    call npm install
    popd
)

echo [1/2] Starting .NET server (dotnet watch)...
start "CRS Server (dotnet watch)" cmd /k "cd /d "%SCRIPT_DIR%" && set CRS_ENV=development && dotnet watch run --project Server\Server.csproj"

echo [2/2] Starting Vite dev server...
start "CRS UI (vite)" cmd /k "cd /d "%SCRIPT_DIR%\ui" && npm run dev"

echo.
echo   Server:  http://localhost:9876
echo   UI:      http://localhost:5173
echo.
echo   Two windows were opened - close them to stop each process.
echo.

endlocal
