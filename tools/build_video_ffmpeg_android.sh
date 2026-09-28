#!/usr/bin/env bash
set -euo pipefail
export PATH="/usr/bin:${PATH}"

: "${FFMPEG_SOURCE:?FFMPEG_SOURCE is required}"
: "${FFMPEG_BUILD:?FFMPEG_BUILD is required}"
: "${FFMPEG_PREFIX:?FFMPEG_PREFIX is required}"
: "${ANDROID_TOOLCHAIN:?ANDROID_TOOLCHAIN is required}"

ANDROID_API_LEVEL="${ANDROID_API_LEVEL:-24}"
BUILD_JOBS="${BUILD_JOBS:-8}"
TARGET="aarch64-linux-android${ANDROID_API_LEVEL}"

mkdir -p "${FFMPEG_BUILD}" "${FFMPEG_PREFIX}"
cd "${FFMPEG_BUILD}"

"${FFMPEG_SOURCE}/configure" \
    --prefix="${FFMPEG_PREFIX}" \
    --target-os=android \
    --arch=aarch64 \
    --enable-cross-compile \
    --cc="${ANDROID_TOOLCHAIN}/bin/clang.exe" \
    --cxx="${ANDROID_TOOLCHAIN}/bin/clang++.exe" \
    --ld="${ANDROID_TOOLCHAIN}/bin/clang.exe" \
    --ar="${ANDROID_TOOLCHAIN}/bin/llvm-ar.exe" \
    --nm="${ANDROID_TOOLCHAIN}/bin/llvm-nm.exe" \
    --ranlib="${ANDROID_TOOLCHAIN}/bin/llvm-ranlib.exe" \
    --strip="${ANDROID_TOOLCHAIN}/bin/llvm-strip.exe" \
    --sysroot="${ANDROID_TOOLCHAIN}/sysroot" \
    --extra-cflags="--target=${TARGET} -fPIC -Os -ffunction-sections -fdata-sections" \
    --extra-ldflags="--target=${TARGET} -Wl,--gc-sections -Wl,-z,max-page-size=16384 -Wl,-z,common-page-size=16384" \
    --extra-libs="-lm -ldl -llog -landroid" \
    --enable-pic \
    --enable-small \
    --enable-shared \
    --disable-static \
    --disable-symver \
    --disable-everything \
    --disable-autodetect \
    --disable-programs \
    --disable-doc \
    --disable-debug \
    --disable-network \
    --disable-avdevice \
    --disable-avfilter \
    --disable-postproc \
    --disable-iconv \
    --disable-zlib \
    --disable-bzlib \
    --disable-lzma \
    --enable-avcodec \
    --enable-avformat \
    --enable-avutil \
    --enable-swresample \
    --enable-swscale \
    --enable-protocol=file \
    --enable-demuxer=asf,mpegps,mpegvideo \
    --enable-parser=vc1,mpegvideo,mpegaudio \
    --enable-bsf=extract_extradata \
    --enable-decoder=wmv1,wmv2,wmv3,vc1,mpeg1video,mpeg2video,mp1,mp2 \
    --enable-decoder=wmav1,wmav2,wmapro,wmalossless,wmavoice

make -j"${BUILD_JOBS}"
make install
