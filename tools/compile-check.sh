#!/usr/bin/env bash
# Compiles all game scripts against the Unity engine assemblies (plus uGUI and TextMeshPro
# built from the editor's package sources) WITHOUT starting Unity. Catches C# errors early.
# Usage: tools/compile-check.sh [webgl]
set -e
cd "$(dirname "$0")/.."
CSC="/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe"
E="C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data"
U="$E/Resources/PackageManager/BuiltInPackages/com.unity.ugui/Runtime"
OUT=Temp/check
mkdir -p "$OUT"

DEFINES="UNITY_6000_0_OR_NEWER;UNITY_6000_3_OR_NEWER;UNITY_2023_3_OR_NEWER;UNITY_2023_2_OR_NEWER;UNITY_2023_1_OR_NEWER;UNITY_2022_3_OR_NEWER;UNITY_2022_2_OR_NEWER;UNITY_2022_1_OR_NEWER;UNITY_2021_3_OR_NEWER;UNITY_2021_2_OR_NEWER;UNITY_2021_1_OR_NEWER;UNITY_2020_3_OR_NEWER;UNITY_2020_2_OR_NEWER;UNITY_2020_1_OR_NEWER;UNITY_2019_4_OR_NEWER;UNITY_2019_3_OR_NEWER;UNITY_2019_2_OR_NEWER;UNITY_2019_1_OR_NEWER;UNITY_2018_4_OR_NEWER;UNITY_2018_3_OR_NEWER;UNITY_2018_2_OR_NEWER;UNITY_2018_1_OR_NEWER;UNITY_2017_1_OR_NEWER;UNITY_5_3_OR_NEWER;UNITY_5_6_OR_NEWER;ENABLE_LEGACY_INPUT_MANAGER;UNITY_UGUI_SHADER_GRAPH"
if [ "$1" = "webgl" ]; then DEFINES="$DEFINES;UNITY_WEBGL;PLATFORM_WEBGL"; fi

REFS=()
for f in "$E"/Managed/UnityEngine/UnityEngine*.dll; do REFS+=("-r:$f"); done
REFS+=("-r:$E/NetStandard/ref/2.1.0/netstandard.dll")
COMMON=(-nologo -nostdlib -noconfig -nowarn:0618,0649,0414,0169,0067,0109 -langversion:9 -define:"$DEFINES" -target:library)

if [ ! -f "$OUT/Unity.TextMeshPro.dll" ]; then
  mapfile -t BRIDGE < <(find "$U/InternalBridge" -name "*.cs")
  "$CSC" "${COMMON[@]}" "${REFS[@]}" -out:"$OUT/Unity.InternalAPIEngineBridge.004.dll" "${BRIDGE[@]}"
  mapfile -t UGUI < <(find "$U/UGUI" -name "*.cs")
  "$CSC" "${COMMON[@]}" "${REFS[@]}" -r:"$OUT/Unity.InternalAPIEngineBridge.004.dll" -out:"$OUT/UnityEngine.UI.dll" "${UGUI[@]}"
  mapfile -t TMP < <(find "$U/TMP" -name "*.cs")
  "$CSC" "${COMMON[@]}" "${REFS[@]}" -r:"$OUT/UnityEngine.UI.dll" -r:"$OUT/Unity.InternalAPIEngineBridge.004.dll" -out:"$OUT/Unity.TextMeshPro.dll" "${TMP[@]}"
fi

mapfile -t GAME < <(find Assets/Scripts -name "*.cs")
"$CSC" "${COMMON[@]}" "${REFS[@]}" -r:"$OUT/UnityEngine.UI.dll" -r:"$OUT/Unity.TextMeshPro.dll" -out:"$OUT/Game.dll" "${GAME[@]}"
echo "compile check OK ($1)"

# editor tools (build script) against the editor assemblies
EREFS=()
for f in "$E"/Managed/UnityEngine/UnityEditor*.dll; do EREFS+=("-r:$f"); done
mapfile -t EDITOR < <(find Assets/Editor -name "*.cs")
"$CSC" "${COMMON[@]}" "${REFS[@]}" "${EREFS[@]}" -r:"$OUT/Game.dll" -r:"$OUT/UnityEngine.UI.dll" -r:"$OUT/Unity.TextMeshPro.dll" -out:"$OUT/Editor.dll" "${EDITOR[@]}"
echo "editor compile check OK"
