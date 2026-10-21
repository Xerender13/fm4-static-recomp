#include <catch2/catch_test_macros.hpp>

#include <cstdlib>

#include <rex/input/input.h>

#include "input/keyboard_ramp.h"

using fm4_static_recomp::input::KeyboardRamp;
using fm4_static_recomp::input::RampRates;
using rex::input::X_INPUT_GAMEPAD;

namespace {

constexpr RampRates kRates = {0.20, 0.12, 0.10, 0.08, 0.15};

X_INPUT_GAMEPAD Raw(int16_t lx = 0, uint8_t rt = 0, int16_t rx = 0) {
  X_INPUT_GAMEPAD pad = {};
  pad.thumb_lx = lx;
  pad.right_trigger = rt;
  pad.thumb_rx = rx;
  return pad;
}

X_INPUT_GAMEPAD Step(KeyboardRamp& ramp, X_INPUT_GAMEPAD raw, double seconds, bool driving = true) {
  ramp.Apply(raw, seconds, driving);
  return raw;
}

bool Near(int value, int expected, int tolerance) {
  return std::abs(value - expected) <= tolerance;
}

}  // namespace

TEST_CASE("Steering rises over steer_seconds", "[keyboard_ramp]") {
  KeyboardRamp ramp(kRates);
  CHECK(Near(Step(ramp, Raw(32767), 0.1).thumb_lx, 16384, 330));
  CHECK(Step(ramp, Raw(32767), 0.1).thumb_lx == 32767);
}

TEST_CASE("A released key returns at the return rate", "[keyboard_ramp]") {
  KeyboardRamp ramp(kRates);
  Step(ramp, Raw(32767), 0.1);
  Step(ramp, Raw(32767), 0.1);
  CHECK(Near(Step(ramp, Raw(0), 0.06).thumb_lx, 16384, 330));
  CHECK(Step(ramp, Raw(0), 0.06).thumb_lx == 0);
}

TEST_CASE("A reversal passes through zero at the return rate first", "[keyboard_ramp]") {
  KeyboardRamp ramp(kRates);
  Step(ramp, Raw(32767), 0.1);
  Step(ramp, Raw(32767), 0.1);
  CHECK(Step(ramp, Raw(-32767), 0.06).thumb_lx > 0);
  CHECK(Step(ramp, Raw(-32767), 0.06).thumb_lx == 0);
  CHECK(Step(ramp, Raw(-32767), 0.05).thumb_lx < 0);
}

TEST_CASE("The pedals use the pedal rates", "[keyboard_ramp]") {
  KeyboardRamp ramp(kRates);
  CHECK(Near(Step(ramp, Raw(0, 255), 0.05).right_trigger, 128, 3));
  CHECK(Step(ramp, Raw(0, 255), 0.05).right_trigger == 255);
  CHECK(Near(Step(ramp, Raw(0, 0), 0.04).right_trigger, 128, 3));
}

TEST_CASE("Looking uses the look rate", "[keyboard_ramp]") {
  KeyboardRamp ramp(kRates);
  CHECK(Near(Step(ramp, Raw(0, 0, -32767), 0.075).thumb_rx, -16384, 330));
  CHECK(Step(ramp, Raw(0, 0, -32767), 0.075).thumb_rx == -32767);
}

TEST_CASE("Menus pass the keys through, and driving starts from them", "[keyboard_ramp]") {
  KeyboardRamp ramp(kRates);
  CHECK(Step(ramp, Raw(32767, 255), 0.01, false).thumb_lx == 32767);
  // Still held as the race starts: no drop to zero and rise again.
  CHECK(Step(ramp, Raw(32767, 255), 0.01).thumb_lx == 32767);
  CHECK(Step(ramp, Raw(32767, 255), 0.01).right_trigger == 255);
}

TEST_CASE("Buttons are untouched", "[keyboard_ramp]") {
  KeyboardRamp ramp(kRates);
  X_INPUT_GAMEPAD raw = Raw(32767);
  raw.buttons = 0x1234;
  raw.thumb_ly = 1000;
  ramp.Apply(raw, 0.01, true);
  CHECK(static_cast<uint16_t>(raw.buttons) == 0x1234);
  CHECK(raw.thumb_ly == 1000);
}

TEST_CASE("Losing focus drops the output to zero at the return rate", "[keyboard_ramp]") {
  KeyboardRamp ramp(kRates);
  Step(ramp, Raw(32767, 255), 0.1);
  Step(ramp, Raw(32767, 255), 0.1);
  // Without focus the driver reports an all-zero state.
  const int first = Step(ramp, Raw(), 0.03).thumb_lx;
  CHECK(first < 32767);
  CHECK(first > 0);
  const X_INPUT_GAMEPAD later = Step(ramp, Raw(), 0.1);
  CHECK(later.thumb_lx == 0);
  CHECK(later.right_trigger == 0);
}

TEST_CASE("A long gap counts as a tenth of a second", "[keyboard_ramp]") {
  KeyboardRamp ramp(kRates);
  CHECK(Near(Step(ramp, Raw(32767), 5.0).thumb_lx, 16384, 330));
}
