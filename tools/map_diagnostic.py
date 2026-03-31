"""
Map diagnostic: runs MapTester self-play via tournament machinery and dumps
per-map action frequencies + positional data to spot which sub-strategies
are actually firing and which map features go unused.
"""

import json, subprocess, sys, os

# We'll use the tournament server to run matches, but it's easier to just
# invoke Godot headless with a custom diagnostic scene.  Instead, let's
# parse the test output more carefully.  Actually the simplest approach:
# add a diagnostic mode to the existing MapTests that dumps JSON.

# For now, let's just re-run the tests and parse the output.
print("This script is a placeholder — the real diagnostics run inside Godot.")
print("Run: Godot_console.exe --headless --scene res://scenes/map_diagnostic.tscn")
