#!/bin/sh
# Run the unit tests with a Command Line Tools-only install (Swift Testing lives outside the SDK).
set -e
cd "$(dirname "$0")/.."
FW=/Library/Developer/CommandLineTools/Library/Developer/Frameworks
LIB=/Library/Developer/CommandLineTools/Library/Developer/usr/lib
exec swift test -Xswiftc -F -Xswiftc "$FW" -Xlinker -rpath -Xlinker "$FW" -Xlinker -rpath -Xlinker "$LIB" "$@"
