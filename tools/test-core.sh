#!/usr/bin/env bash
# Compiles the game rules (no Unity needed) together with Tests~/CoreTests.cs and runs them.
set -e
cd "$(dirname "$0")/.."
CSC="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe"
mkdir -p Temp
"$CSC" -nologo -optimize -out:Temp/CoreTests.exe Assets/Scripts/Core/Arena.cs "Tests~/CoreTests.cs"
./Temp/CoreTests.exe
