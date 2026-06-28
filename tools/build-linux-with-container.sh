#!/usr/bin/env bash
set -euo pipefail

rid="${1:-linux-arm64}"

case "${rid}" in
  linux-arm64)
    container_arch="arm64"
    ;;
  linux-x64)
    container_arch="amd64"
    ;;
  *)
    echo "Usage: $0 [linux-arm64|linux-x64]" >&2
    exit 2
    ;;
esac

if ! command -v container >/dev/null 2>&1; then
  echo "container CLI not found. Install Apple Container first." >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
image="mcr.microsoft.com/dotnet/sdk:10.0"
cmake_build_dir="native/build/cmake-container-${rid}"

container run --arch "${container_arch}" --rm \
  -v "${repo_root}:/workspace" \
  -w /workspace \
  "${image}" \
  bash -lc "
    set -euo pipefail
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends cmake build-essential
    cmake -S native -B ${cmake_build_dir} -DCMAKE_BUILD_TYPE=Release
    cmake --build ${cmake_build_dir} --config Release

    audio_runtime=\"native/build/runtimes/${rid}/native\"
    publish_native=\"src/BubiBoy.App/bin/Release/net10.0/${rid}/publish/runtimes/${rid}/native\"

    mkdir -p \"\${audio_runtime}\"
    cp ${cmake_build_dir}/libbubi_miniaudio.so \"\${audio_runtime}/\"

    DOTNET_CLI_HOME=/workspace/.dotnet-cli-home dotnet restore BubiBoy.slnx
    DOTNET_CLI_HOME=/workspace/.dotnet-cli-home dotnet publish src/BubiBoy.App/BubiBoy.App.fsproj -c Release -r ${rid} --no-restore --self-contained true

    mkdir -p \"\${publish_native}\"
    cp \"\${audio_runtime}/libbubi_miniaudio.so\" \"\${publish_native}/\"
  "

echo "Published output: src/BubiBoy.App/bin/Release/net10.0/${rid}/publish"
