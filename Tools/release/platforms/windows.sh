# Windows operator server; see Tools/release/lib/desktop-operator.sh.
OPERATOR_OS=Windows
OPERATOR_MODULE=WindowsStandaloneSupport
PLATFORM_PROFILE="Assets/Settings/Build Profiles/Windows Operator.asset"
PLATFORM_BUILD_TARGET=Win64
PLATFORM_OUTPUT="Builds/Release/Windows/Lasertag.exe"
source "$release_dir/lib/desktop-operator.sh"
