# Forza Motorsport 4 - native ShiftGlue host

cmake_minimum_required(VERSION 3.25)
project(fm4_static_recomp LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# SDL uses native Windows HID and XInput backends here; its optional libusb
# probe should not make a normal Windows build look like it lacks a dependency.
set(SDL_HIDAPI_LIBUSB OFF CACHE BOOL "Disable optional libusb HID backend on Windows" FORCE)

include(cmake/Fm4RexGlue.cmake)

add_executable(fm4_static_recomp WIN32
    src/main.cpp
    src/built_in_defaults.cpp
    src/tool_mode.cpp
    src/disc2_install.cpp
    src/game_hooks.cpp
    src/scripted_input.cpp
    src/shader_cache/cache_writer.cpp
    src/shader_cache/disc_shaders.cpp
    src/config/host_config.cpp
    src/ui/graphics_page.cpp
    src/ui/hostui/glyph_atlas.cpp
    src/ui/hostui/host_ui.cpp
    src/ui/hostui/menu.cpp
    src/ui/hostui/ui_archive.cpp
    src/ui/hostui/vector_font.cpp
    src/ui/hostui/xds_texture.cpp
)

fm4_static_recomp_attach_rexglue(fm4_static_recomp)
target_link_libraries(fm4_static_recomp PRIVATE d3d12 dxgi rex::gpu-fh1)

fm4_static_recomp_add_generated_module(
    fm4_static_recomp_SpeechFacade_default speech
    "${FM4_STATIC_RECOMP_SPEECH_GENERATED_SOURCES}")
fm4_static_recomp_add_generated_module(
    fm4_static_recomp_XMediaFacade_default xmedia
    "${FM4_STATIC_RECOMP_XMEDIA_GENERATED_SOURCES}")
