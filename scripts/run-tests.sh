#!/usr/bin/env bash
# run-tests.sh — Build and test ElBruno.S1Mini on Linux/macOS/WSL

set -euo pipefail

if [ -t 1 ]; then
    RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
else
    RED=''; GREEN=''; YELLOW=''; CYAN=''; NC=''
fi

SKIP_BUILD=false
SKIP_UNIT=false
FRAMEWORK="net8.0"
FILTER=""

show_help() {
    cat <<EOF
Usage: $(basename "$0") [options]

Options:
  --skip-build, -B              Skip the dotnet build step
  --no-build                    Alias for --skip-build
  --skip-unit-tests, -U         Skip unit tests
  --framework <value>           Target framework (default: net8.0)
  --filter <value>              xUnit filter string
  --help, -h                    Show this help message
EOF
    exit 0
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --skip-build|-B|--no-build) SKIP_BUILD=true; shift ;;
        --skip-unit-tests|-U) SKIP_UNIT=true; shift ;;
        --framework) FRAMEWORK="${2:?'--framework requires a value'}"; shift 2 ;;
        --filter) FILTER="${2:?'--filter requires a value'}"; shift 2 ;;
        --help|-h) show_help ;;
        *) echo -e "${RED}Unknown option: $1${NC}" >&2; show_help ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT=""
SEARCH_DIR="$SCRIPT_DIR"

while [[ "$SEARCH_DIR" != "/" ]]; do
    if [[ -f "$SEARCH_DIR/ElBruno.S1Mini.slnx" ]]; then
        REPO_ROOT="$SEARCH_DIR"
        break
    fi
    SEARCH_DIR="$(dirname "$SEARCH_DIR")"
done

if [[ -z "$REPO_ROOT" ]]; then
    echo -e "${RED}ERROR: Could not find ElBruno.S1Mini.slnx — repo root not found.${NC}" >&2
    exit 99
fi

SOLUTION="$REPO_ROOT/ElBruno.S1Mini.slnx"
UNIT_TEST_PROJ="$REPO_ROOT/src/tests/ElBruno.S1Mini.Tests/ElBruno.S1Mini.Tests.csproj"

START_SECONDS=$SECONDS
echo -e "${CYAN}========================================${NC}"
echo -e "${CYAN} $(basename "$0")  |  $(date '+%Y-%m-%d %H:%M:%S')${NC}"
echo -e "${CYAN} Repo root: $REPO_ROOT${NC}"
echo -e "${CYAN} Framework: $FRAMEWORK${NC}"
echo -e "${CYAN}========================================${NC}"

if [[ "$SKIP_BUILD" == "false" ]]; then
    echo -e "\n${YELLOW}>>> Build${NC}"
    if ! dotnet build "$SOLUTION"; then
        echo -e "${RED}ERROR: Build failed.${NC}" >&2
        exit 1
    fi
    echo -e "${GREEN}Build succeeded.${NC}"
else
    echo -e "${YELLOW}>>> Build skipped.${NC}"
fi

if [[ "$SKIP_UNIT" == "false" ]]; then
    echo -e "\n${YELLOW}>>> Unit tests${NC}"
    if [[ -n "$FILTER" ]]; then
        dotnet test "$UNIT_TEST_PROJ" --framework "$FRAMEWORK" --no-build --logger "console;verbosity=minimal" --filter "$FILTER"
    else
        dotnet test "$UNIT_TEST_PROJ" --framework "$FRAMEWORK" --no-build --logger "console;verbosity=minimal"
    fi
    echo -e "${GREEN}Unit tests passed.${NC}"
else
    echo -e "${YELLOW}>>> Unit tests skipped.${NC}"
fi

ELAPSED=$(( SECONDS - START_SECONDS ))
echo -e "\n${GREEN}========================================${NC}"
echo -e "${GREEN} All checks passed in ${ELAPSED}s${NC}"
echo -e "${GREEN}========================================${NC}"
exit 0
