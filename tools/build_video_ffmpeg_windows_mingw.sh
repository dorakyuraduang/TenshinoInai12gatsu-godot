#!/usr/bin/env bash
set -euo pipefail

: "${FFMPEG_SOURCE:?FFMPEG_SOURCE is required}"
: "${FFMPEG_BUILD:?FFMPEG_BUILD is required}"
: "${FFMPEG_PREFIX:?FFMPEG_PREFIX is required}"
: "${MINGW_BIN:?MINGW_BIN is required}"

BUILD_JOBS="${BUILD_JOBS:-8}"
export PATH="${MINGW_BIN}:/usr/bin:${PATH}"

mkdir -p "${FFMPEG_BUILD}" "${FFMPEG_PREFIX}"
cd "${FFMPEG_BUILD}"

"${FFMPEG_SOURCE}/configure" \
    --prefix="${FFMPEG_PREFIX}" \
    --target-os=mingw32 \
    --arch=x86_64 \
    --cc="${MINGW_BIN}/gcc.exe" \
    --cxx="${MINGW_BIN}/g++.exe" \
    --ar="${MINGW_BIN}/ar.exe" \
    --nm="${MINGW_BIN}/nm.exe" \
    --ranlib="${MINGW_BIN}/ranlib.exe" \
    --strip="${MINGW_BIN}/strip.exe" \
    --extra-cflags="-Os -ffunction-sections -fdata-sections" \
    --extra-ldflags="-Wl,--gc-sections" \
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
