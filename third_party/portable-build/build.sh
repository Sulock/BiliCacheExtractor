#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-only
# Some dependency options follow BtbN recipes; their MIT notice is retained in
# third_party/ffmpeg-builds.LICENSE and the resulting source kit.
set -euo pipefail
recipe_dir=$(cd "$(dirname "$0")" && pwd)
work=${BCE_FFMPEG_WORK:-"${RUNNER_TEMP:-/tmp}/bce-ffmpeg-8.1.3"}
mkdir -p "$work"/{src,prefix,out,source-kit}
prefix="$work/prefix"
export CC=x86_64-w64-mingw32-gcc-posix CXX=x86_64-w64-mingw32-g++-posix
export AR=x86_64-w64-mingw32-ar RANLIB=x86_64-w64-mingw32-ranlib
export PKG_CONFIG_LIBDIR="$prefix/lib/pkgconfig" PKG_CONFIG_PATH=""
jobs=${BCE_BUILD_JOBS:-$(nproc)}
cp "$recipe_dir"/{build.sh,sources.json,finish.py} "$work/source-kit/"
cp "$recipe_dir/../ffmpeg-builds.LICENSE" "$work/source-kit/BtbN-recipes.LICENSE"
python3 - "$recipe_dir/sources.json" "$work" <<'PY'
import json, pathlib, subprocess, sys
manifest=json.load(open(sys.argv[1])); root=pathlib.Path(sys.argv[2])
for name, item in manifest.items():
    target=root/'src'/name
    target.mkdir(exist_ok=True)
    def git(*args): subprocess.run(['git','-C',str(target),*args], check=True)
    if not (target/'.git').exists():
        git('init'); git('remote','add','origin',item['repository'])
    git('fetch','--depth=1','origin',item['revision'])
    git('checkout','--detach','FETCH_HEAD')
    actual=subprocess.check_output(['git','-C',str(target),'rev-parse','HEAD'],text=True).strip()
    if actual != item['revision']: raise RuntimeError('Source revision mismatch')
    if name=='ffmpeg': git('tag','-f','n8.1.3')
    git('archive','--format=tar.gz','--prefix='+name+'/',
        '--output='+str(root/'source-kit'/(name+'-source.tar.gz')),'HEAD')
PY
cat > "$work/toolchain.cmake" <<EOF
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)
set(CMAKE_C_COMPILER $CC)
set(CMAKE_CXX_COMPILER $CXX)
set(CMAKE_RC_COMPILER x86_64-w64-mingw32-windres)
set(CMAKE_FIND_ROOT_PATH "$prefix" /usr/x86_64-w64-mingw32)
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_EXE_LINKER_FLAGS "-static -static-libgcc -static-libstdc++")
EOF
cat > "$work/cross.meson" <<EOF
[binaries]
c = '$CC'
cpp = '$CXX'
ar = '$AR'
strip = 'x86_64-w64-mingw32-strip'
windres = 'x86_64-w64-mingw32-windres'
pkgconfig = 'pkg-config'
[host_machine]
system = 'windows'
cpu_family = 'x86_64'
cpu = 'x86_64'
endian = 'little'
[properties]
needs_exe_wrapper = true
EOF
cp "$work"/{toolchain.cmake,cross.meson} "$work/source-kit/"
make -C "$work/src/nv-codec-headers" PREFIX="$prefix" install
mkdir -p "$prefix/include/AMF"
cp -a "$work/src/amf/amf/public/include/." "$prefix/include/AMF/"
(
  cd "$work/src/zlib"
  ./configure --prefix="$prefix" --static
  make -j"$jobs" && make install
)
(
  cd "$work/src/x264"
  ./configure --host=x86_64-w64-mingw32 --cross-prefix=x86_64-w64-mingw32- \
    --prefix="$prefix" --enable-static --disable-cli --disable-lavf --disable-swscale
  make -j"$jobs" && make install
)
# Preserve this build fix alongside the original dependency source.
sed -i '1i#include <cstdint>' "$work/src/x265/source/dynamicHDR10/json11/json11.cpp"
git -C "$work/src/x265" diff > "$work/source-kit/x265-build.patch"
cmake -S "$work/src/x265/source" -B "$work/x265-build" -G Ninja \
  -DCMAKE_TOOLCHAIN_FILE="$work/toolchain.cmake" -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_INSTALL_PREFIX="$prefix" -DENABLE_SHARED=OFF -DENABLE_CLI=OFF \
  -DENABLE_HDR10_PLUS=ON -DCMAKE_ASM_NASM_FLAGS=-w-macro-params-legacy
ninja -C "$work/x265-build" -j"$jobs"
ninja -C "$work/x265-build" install
# x265 omits pkg-config metadata for a Windows static-only build.
cat > "$prefix/lib/pkgconfig/x265.pc" <<EOF
prefix=$prefix
libdir=\${prefix}/lib
includedir=\${prefix}/include
Name: x265
Description: H.265/HEVC video encoder
Version: $(git -C "$work/src/x265" rev-parse --short HEAD)
Libs: -L\${libdir} -lx265
Libs.private: -lstdc++ -lwinpthread
Cflags: -I\${includedir}
EOF
meson setup "$work/dav1d-build" "$work/src/dav1d" --cross-file "$work/cross.meson" \
  --prefix "$prefix" --libdir lib --buildtype release --default-library static \
  -Denable_tools=false -Denable_tests=false
ninja -C "$work/dav1d-build" -j"$jobs"
ninja -C "$work/dav1d-build" install
sed -i 's/^#if _MSC_VER < 1400$/#if defined(_MSC_VER) \&\& _MSC_VER < 1400/' "$work/src/libvpl/libvpl/src/windows/mfx_dispatcher_defs.h"
git -C "$work/src/libvpl" diff > "$work/source-kit/libvpl-build.patch"
cmake -S "$work/src/libvpl" -B "$work/vpl-build" -G Ninja \
  -DCMAKE_TOOLCHAIN_FILE="$work/toolchain.cmake" -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_INSTALL_PREFIX="$prefix" -DCMAKE_INSTALL_LIBDIR=lib \
  -DBUILD_DISPATCHER=ON -DBUILD_DEV=ON -DBUILD_PREVIEW=OFF -DBUILD_TOOLS=OFF \
  -DBUILD_TOOLS_ONEVPL_EXPERIMENTAL=OFF -DINSTALL_EXAMPLE_CODE=OFF \
  -DBUILD_SHARED_LIBS=OFF -DBUILD_TESTS=OFF
ninja -C "$work/vpl-build" -j"$jobs"
ninja -C "$work/vpl-build" install
echo 'Libs.private: -lstdc++ -lwinpthread' >> "$prefix/lib/pkgconfig/vpl.pc"
(
  cd "$work/src/ffmpeg"
  ./configure --prefix="$work/runtime" --arch=x86_64 --target-os=mingw32 \
    --cross-prefix=x86_64-w64-mingw32- --cc="$CC" --cxx="$CXX" --pkg-config=pkg-config \
    --pkg-config-flags=--static --disable-autodetect --disable-debug --disable-doc \
    --disable-shared --enable-static --disable-ffplay --enable-gpl --enable-version3 \
    --enable-libx264 --enable-libx265 --enable-libdav1d --enable-libvpl --enable-amf \
    --enable-ffnvcodec --enable-nvenc --enable-nvdec --enable-cuvid --enable-zlib \
    --enable-schannel --extra-cflags="-I$prefix/include" \
    --extra-ldflags="-L$prefix/lib -static -static-libgcc -static-libstdc++" \
    --extra-libs='-lstdc++ -lwinpthread' --extra-version=bce-nv12.2
  cp ffbuild/config.log "$work/source-kit/ffmpeg-config.log"
  cp ffbuild/config.mak "$work/source-kit/ffmpeg-config.mak"
  make -j"$jobs"
  make install
)
dpkg-query -W > "$work/source-kit/build-environment.txt"
python3 "$recipe_dir/finish.py" "$work" "$recipe_dir/sources.json"
