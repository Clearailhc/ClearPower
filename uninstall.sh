#!/usr/bin/env bash
set -uo pipefail
gnome-extensions disable clearpower@lhc 2>/dev/null || true
sudo apt remove -y clearpower
echo "removed (sudo apt purge clearpower also deletes /var/lib/clearpower state)"
