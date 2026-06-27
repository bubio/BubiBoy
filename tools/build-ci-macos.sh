#!/usr/bin/env bash
set -euo pipefail

rid="${1:-osx-arm64}"

case "${rid}" in
  osx-arm64|osx-x64) ;;
  *)
    echo "Usage: $0 [osx-arm64|osx-x64]" >&2
    exit 2
    ;;
esac

audio_build="native/build/cmake"
audio_runtime="native/build/runtimes/${rid}/native"
ra_build="native/bubi_rcheevos/build/cmake/${rid}"
ra_runtime="native/bubi_rcheevos/build/runtimes/${rid}/native"
publish_path="src/BubiBoy.App/bin/Release/${rid}/BubiBoy.app"
publish_native="${publish_path}/Contents/MacOS/runtimes/${rid}/native"

cmake -S native -B "${audio_build}" -DCMAKE_BUILD_TYPE=Release
cmake --build "${audio_build}" --config Release
mkdir -p "${audio_runtime}"
cp "${audio_build}/libbubi_miniaudio.dylib" "${audio_runtime}/"

cmake -S native/bubi_rcheevos -B "${ra_build}" -DCMAKE_BUILD_TYPE=Release
cmake --build "${ra_build}" --config Release
ctest --test-dir "${ra_build}" --output-on-failure
mkdir -p "${ra_runtime}"
cp "${ra_build}/libbubi_rcheevos.dylib" "${ra_runtime}/"

DOTNET_CLI_HOME="${PWD}/.dotnet-cli-home" dotnet restore BubiBoy.slnx
DOTNET_CLI_HOME="${PWD}/.dotnet-cli-home" dotnet build BubiBoy.slnx --no-restore
BUBIBOY_EXPECT_NATIVE_AUDIO=1 \
  DOTNET_CLI_HOME="${PWD}/.dotnet-cli-home" \
  dotnet test BubiBoy.slnx --no-build
DOTNET_CLI_HOME="${PWD}/.dotnet-cli-home" \
  dotnet publish src/BubiBoy.App/BubiBoy.App.fsproj \
  -c Release -r "${rid}" --no-restore --self-contained true

mkdir -p "${publish_native}"
cp "${audio_runtime}/"* "${publish_native}/"
cp "${ra_runtime}/"* "${publish_native}/"

test -f "${publish_path}/Contents/MacOS/libbubi_rcheevos.dylib"
test -f "${publish_native}/libbubi_miniaudio.dylib"
test -f "${publish_native}/libbubi_rcheevos.dylib"
codesign --force --deep --sign - "${publish_path}"

echo "CI-equivalent bundle: ${publish_path}"
