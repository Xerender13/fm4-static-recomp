#pragma once

namespace fm4_static_recomp {

// Launcher helpers that run the executable without starting the game.
enum class ToolMode { kNone, kInstall, kSchema, kShaderCache, kUiFonts };

ToolMode CurrentToolMode();

}  // namespace fm4_static_recomp
