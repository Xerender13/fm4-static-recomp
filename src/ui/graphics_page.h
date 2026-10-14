#pragma once

#include <cstdint>
#include <functional>
#include <memory>
#include <optional>
#include <utility>

#include "config/host_config.h"
#include "ui/hostui/menu.h"

namespace fm4_native::ui {

// The GRAPHICS page of My Profile -> Options: the race frame rate (a slider),
// preset, resolution scale, post-process anti-aliasing, the game's MSAA,
// anisotropic filtering and more. Each change sets its setting and saves
// `<state>/config/graphics.toml`, which holds only these settings.

// Sets GAME MSAA's default (OFF), then applies the file's values, skipping
// settings given on the command line and values the page does not offer.
// Before the GPU is created.
void ApplySavedGraphicsSettings(const config::HostConfig& config);

// The refresh rate of the display showing `native_window` (an HWND), or 0.
uint32_t DisplayRefreshHz(void* native_window);

// Lowers a saved frame rate above the display's refresh rate (0 = unknown) to
// it, for this session only.
void LimitFrameRateToDisplay(uint32_t refresh_hz);

struct GraphicsPageServices {
  // The refresh rate of the display showing the window when the page opened
  // (0 when unknown).
  std::function<uint32_t()> display_refresh_hz;
  // The draw resolution scale the renderer uses (0 when unknown).
  std::function<uint32_t()> draw_resolution_scale;
  // Where the latest frame reached the window, in pixels.
  std::function<std::optional<std::pair<uint32_t, uint32_t>>()> output_size;
  // After each successful save.
  std::function<void()> saved;
};

std::unique_ptr<hostui::MenuScreen> CreateGraphicsPage(config::HostConfig& config,
                                                       GraphicsPageServices services);

}  // namespace fm4_native::ui
