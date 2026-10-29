# x86-64 Linux against the Steam Runtime 3 "sniper" SDK sysroot
# (adapted from Pinyon Shift, BSD-3: see THIRD_PARTY_NOTICES.md).
#
# The pinned LLVM targets x86_64-linux-gnu inside the sysroot, so the binary
# needs glibc 2.31 at most and runs on SteamOS and current distributions. GCC
# 14's libstdc++ (C++23) is linked statically; libgcc_s.so.1 is on every
# glibc system. Nothing from the build host's own libraries is used.
#
# FM4_LINUX_TOOLCHAIN (cache or environment) holds:
#   llvm/          LLVM 20.1.8 for the build host
#   sniper-sdk/    the SDK sysroot, absolute symlinks made relative
#   sniper-libgcc/ libgcc_s.so (linker script) and libgcc_eh.a from GCC 10,
#                  which the sysroot ships only beside GCC 10, not GCC 14
# or "system" (below).
if(NOT FM4_LINUX_TOOLCHAIN)
  if(DEFINED ENV{FM4_LINUX_TOOLCHAIN})
    set(FM4_LINUX_TOOLCHAIN "$ENV{FM4_LINUX_TOOLCHAIN}")
  else()
    get_filename_component(_fm4_root "${CMAKE_CURRENT_LIST_DIR}/../.." ABSOLUTE)
    set(FM4_LINUX_TOOLCHAIN "${_fm4_root}/.local/toolchain/linux-x86_64")
  endif()
endif()
set(FM4_LINUX_TOOLCHAIN "${FM4_LINUX_TOOLCHAIN}" CACHE STRING
    "Pinned LLVM, sniper sysroot and libgcc overlay for Linux builds, or system")
# try_compile projects reload this file without the cache.
list(APPEND CMAKE_TRY_COMPILE_PLATFORM_VARIABLES FM4_LINUX_TOOLCHAIN)

# "system": the clang, lld and libraries of the machine (or container) that builds, for
# example the fm4-build distrobox that tools/fm4_linux.py --container uses when downloading
# the 3 GB of pinned tools is not practical. The game then needs that machine's glibc or newer.
if(FM4_LINUX_TOOLCHAIN STREQUAL "system")
  set(CMAKE_C_COMPILER clang)
  set(CMAKE_CXX_COMPILER clang++)
  set(CMAKE_EXE_LINKER_FLAGS_INIT "-fuse-ld=lld")
  set(CMAKE_SHARED_LINKER_FLAGS_INIT "-fuse-ld=lld")
  set(CMAKE_MODULE_LINKER_FLAGS_INIT "-fuse-ld=lld")
  return()
endif()

set(CMAKE_SYSTEM_NAME Linux)
set(CMAKE_SYSTEM_PROCESSOR x86_64)

set(_fm4_llvm "${FM4_LINUX_TOOLCHAIN}/llvm")
set(CMAKE_SYSROOT "${FM4_LINUX_TOOLCHAIN}/sniper-sdk")
if(CMAKE_HOST_WIN32)
  set(_fm4_exe ".exe")
else()
  set(_fm4_exe "")
endif()
set(CMAKE_C_COMPILER "${_fm4_llvm}/bin/clang${_fm4_exe}")
set(CMAKE_CXX_COMPILER "${_fm4_llvm}/bin/clang++${_fm4_exe}")
set(CMAKE_ASM_COMPILER "${CMAKE_C_COMPILER}")
set(CMAKE_AR "${_fm4_llvm}/bin/llvm-ar${_fm4_exe}" CACHE FILEPATH "")
set(CMAKE_RANLIB "${_fm4_llvm}/bin/llvm-ranlib${_fm4_exe}" CACHE FILEPATH "")
foreach(_fm4_lang C CXX ASM)
  set(CMAKE_${_fm4_lang}_COMPILER_TARGET x86_64-linux-gnu)
  # Passed as --gcc-toolchain: clang takes GCC 14's headers and libstdc++
  # from here instead of GCC 10's in the sysroot's /usr/lib/gcc.
  set(CMAKE_${_fm4_lang}_COMPILER_EXTERNAL_TOOLCHAIN "${CMAKE_SYSROOT}/usr/lib/gcc-14")
endforeach()

# Paths are quoted: an install folder with spaces ("Non-Steam Games" on an
# SD card) split them into separate arguments.
set(_fm4_link "-fuse-ld=lld \"-L${FM4_LINUX_TOOLCHAIN}/sniper-libgcc\" -static-libstdc++")
# On a Linux host, setup's bin/ wrappers run ld.lld and wayland-scanner with
# the sysroot's libxml2.so.2, which current distributions no longer ship.
set(_fm4_wrappers "${FM4_LINUX_TOOLCHAIN}/bin")
if(NOT CMAKE_HOST_WIN32 AND EXISTS "${_fm4_wrappers}/ld.lld")
  string(APPEND _fm4_link " \"--ld-path=${_fm4_wrappers}/ld.lld\"")
endif()
set(CMAKE_EXE_LINKER_FLAGS_INIT "${_fm4_link}")
set(CMAKE_SHARED_LINKER_FLAGS_INIT "${_fm4_link}")
set(CMAKE_MODULE_LINKER_FLAGS_INIT "${_fm4_link}")

set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE ONLY)

# pkg-config reads only the sysroot's .pc files.
#
# CMake splits pkg-config's -I/-L output at spaces, so a sysroot under a
# folder with spaces ("Non-Steam Games" on an SD card) turns into relative
# include directories and SDL3's configure fails. pkg-config
# is given a space-free symlink to the sysroot instead; the compiler still
# gets CMAKE_SYSROOT itself and reaches the same files through the link.
set(_fm4_pc_sysroot "${CMAKE_SYSROOT}")
if(_fm4_pc_sysroot MATCHES "[ \t]")
  string(MD5 _fm4_pc_hash "${_fm4_pc_sysroot}")
  string(SUBSTRING "${_fm4_pc_hash}" 0 12 _fm4_pc_hash)
  if(DEFINED ENV{XDG_CACHE_HOME} AND NOT "$ENV{XDG_CACHE_HOME}" MATCHES "[ \t]")
    set(_fm4_pc_base "$ENV{XDG_CACHE_HOME}")
  elseif(DEFINED ENV{HOME} AND NOT "$ENV{HOME}" MATCHES "[ \t]")
    set(_fm4_pc_base "$ENV{HOME}/.cache")
  else()
    set(_fm4_pc_base "/tmp")
  endif()
  set(_fm4_pc_link "${_fm4_pc_base}/fm4-static-recomp/sysroot-${_fm4_pc_hash}")
  if(NOT IS_SYMLINK "${_fm4_pc_link}")
    file(MAKE_DIRECTORY "${_fm4_pc_base}/fm4-static-recomp")
    file(CREATE_LINK "${CMAKE_SYSROOT}" "${_fm4_pc_link}" SYMBOLIC)
  endif()
  if(IS_SYMLINK "${_fm4_pc_link}")
    set(_fm4_pc_sysroot "${_fm4_pc_link}")
  else()
    message(WARNING "Could not link ${_fm4_pc_link} to the sysroot; "
            "pkg-config paths with spaces will fail. Install to a folder "
            "without spaces.")
  endif()
endif()
set(ENV{PKG_CONFIG_SYSROOT_DIR} "${_fm4_pc_sysroot}")
set(ENV{PKG_CONFIG_LIBDIR}
    "${_fm4_pc_sysroot}/usr/lib/x86_64-linux-gnu/pkgconfig:${_fm4_pc_sysroot}/usr/share/pkgconfig")
set(ENV{PKG_CONFIG_PATH} "")
# SteamOS has no pkg-config of its own; the sysroot's runs on any host with
# GLib, which every Linux desktop has.
if(NOT CMAKE_HOST_WIN32 AND NOT PKG_CONFIG_EXECUTABLE)
  find_program(_fm4_host_pkg_config NAMES pkg-config pkgconf NO_CMAKE_FIND_ROOT_PATH)
  if(NOT _fm4_host_pkg_config)
    set(PKG_CONFIG_EXECUTABLE "${CMAKE_SYSROOT}/usr/bin/pkg-config" CACHE FILEPATH "")
  endif()
endif()

# SDL3's Wayland backend generates its protocol code at build time. The
# sysroot's scanner runs on a Linux build host with libxml2.
if(NOT CMAKE_HOST_WIN32 AND NOT WAYLAND_SCANNER)
  if(EXISTS "${_fm4_wrappers}/wayland-scanner")
    set(WAYLAND_SCANNER "${_fm4_wrappers}/wayland-scanner" CACHE FILEPATH "")
  else()
    set(WAYLAND_SCANNER "${CMAKE_SYSROOT}/usr/bin/wayland-scanner" CACHE FILEPATH "")
  endif()
endif()
