# Source this file (". scripts/env.sh") to keep .NET/NuGet caches off the system disk.
export LIVEQUIZ_CACHE_ROOT="${LIVEQUIZ_CACHE_ROOT:-/Volumes/DEVNAFRU/DevData/dotnet-cache}"
export DOTNET_CLI_HOME="$LIVEQUIZ_CACHE_ROOT/dotnet-home"
export NUGET_PACKAGES="$LIVEQUIZ_CACHE_ROOT/nuget-packages"
export NUGET_HTTP_CACHE_PATH="$LIVEQUIZ_CACHE_ROOT/nuget-http"
export NUGET_PLUGINS_CACHE_PATH="$LIVEQUIZ_CACHE_ROOT/nuget-plugins"
export NUGET_SCRATCH="$LIVEQUIZ_CACHE_ROOT/nuget-scratch"
export TMPDIR="$LIVEQUIZ_CACHE_ROOT/tmp/"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export PLAYWRIGHT_BROWSERS_PATH="$LIVEQUIZ_CACHE_ROOT/playwright"
