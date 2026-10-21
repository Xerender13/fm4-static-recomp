#pragma once

#include <array>
#include <cstddef>
#include <string>

namespace fm4_static_recomp::config {
class HostConfig;
}

namespace fm4_static_recomp::input {

enum class PadControl {
  kA,
  kB,
  kX,
  kY,
  kLeftShoulder,
  kRightShoulder,
  kLeftTrigger,
  kRightTrigger,
  kLStickUp,
  kLStickDown,
  kLStickLeft,
  kLStickRight,
  kLStickPress,
  kRStickUp,
  kRStickDown,
  kRStickLeft,
  kRStickRight,
  kRStickPress,
  kDpadUp,
  kDpadDown,
  kDpadLeft,
  kDpadRight,
  kBack,
  kStart,
  kCount
};

// The keys of each pad control as the SDK keyboard driver reads them: its
// keybind_* syntax, alternatives separated by commas ("W,Up").
struct BindingSet {
  std::array<std::string, size_t(PadControl::kCount)> keys;
};

// Seconds from rest to full and back, for the smoothed keyboard driving.
struct RampRates {
  double steer_seconds;
  double steer_return_seconds;
  double pedal_seconds;
  double pedal_return_seconds;
  double look_seconds;
};

struct Controls {
  BindingSet menu, driving;
  RampRates ramp;
};

Controls DefaultControls();
// controls.toml: menu_<control> and driving_<control> (a, b, x, y,
// left_shoulder, right_shoulder, left_trigger, right_trigger, lstick_up,
// lstick_down, lstick_left, lstick_right, lstick_press, rstick_*, dpad_up,
// dpad_down, dpad_left, dpad_right, back, start) and ramp_steer_seconds,
// ramp_steer_return_seconds, ramp_pedal_seconds, ramp_pedal_return_seconds,
// ramp_look_seconds. A missing, empty or unreadable value keeps the default.
Controls LoadControls(const config::HostConfig& config);

// Hands the set to the keyboard driver in place of its keybind_* options.
void ApplyBindings(const BindingSet& set);

}  // namespace fm4_static_recomp::input
