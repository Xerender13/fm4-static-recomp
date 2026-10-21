#pragma once

#include <rex/input/input.h>

#include "input/controls.h"

namespace fm4_static_recomp::input {

// Smooths the keyboard's all-or-nothing steering, pedals and look while
// driving: each axis moves toward the keys' value at full scale per its rate,
// back toward zero at the return rate, and through zero first on a reversal.
// Menus pass the keys through unchanged.
class KeyboardRamp {
 public:
  explicit KeyboardRamp(RampRates rates);
  void Apply(rex::input::X_INPUT_GAMEPAD& pad, double elapsed_seconds, bool driving);

 private:
  RampRates rates_;
  double steer_ = 0.0;
  double left_trigger_ = 0.0;
  double right_trigger_ = 0.0;
  double look_x_ = 0.0;
  double look_y_ = 0.0;
};

}  // namespace fm4_static_recomp::input
