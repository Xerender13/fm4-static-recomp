# Forza Motorsport 4 - native ShiftGlue host

cmake_minimum_required(VERSION 3.25)
project(fm4_native LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# SDL uses native Windows HID and XInput backends here; its optional libusb
# probe should not make a normal Windows build look like it lacks a dependency.
set(SDL_HIDAPI_LIBUSB OFF CACHE BOOL "Disable optional libusb HID backend on Windows" FORCE)

include(cmake/Fm4RexGlue.cmake)

add_executable(fm4_native WIN32
    src/main.cpp
    src/built_in_defaults.cpp
    src/disc2_install.cpp
    src/game_hooks.cpp
    src/scripted_input.cpp
    src/config/host_config.cpp
    src/ui/graphics_page.cpp
    src/ui/hostui/glyph_atlas.cpp
    src/ui/hostui/host_ui.cpp
    src/ui/hostui/menu.cpp
    src/ui/hostui/ui_archive.cpp
    src/ui/hostui/vector_font.cpp
    src/ui/hostui/xds_texture.cpp
)

fm4_native_attach_rexglue(fm4_native)
target_link_libraries(fm4_native PRIVATE d3d12 dxgi rex::gpu-fh1)

fm4_native_add_generated_module(
    fm4_native_SpeechFacade_default speech
    "${FM4_NATIVE_SPEECH_GENERATED_SOURCES}")
fm4_native_add_generated_module(
    fm4_native_XMediaFacade_default xmedia
    "${FM4_NATIVE_XMEDIA_GENERATED_SOURCES}")
