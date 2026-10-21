#include "input/controls.h"

#include <charconv>
#include <memory>
#include <string_view>

#include <rex/input/mnk/mnk_bind.h>
#include <rex/logging.h>
#include <rex/ui/keybinds.h>

#include "config/host_config.h"

namespace fm4_static_recomp::input {
namespace {

struct ControlNames {
  const char* file;    // controls.toml name after menu_ / driving_
  const char* option;  // the SDK keyboard driver's option
};

constexpr std::array<ControlNames, size_t(PadControl::kCount)> kNames = {{
    {"a", "keybind_a"},
    {"b", "keybind_b"},
    {"x", "keybind_x"},
    {"y", "keybind_y"},
    {"left_shoulder", "keybind_left_shoulder"},
    {"right_shoulder", "keybind_right_shoulder"},
    {"left_trigger", "keybind_left_trigger"},
    {"right_trigger", "keybind_right_trigger"},
    {"lstick_up", "keybind_lstick_up"},
    {"lstick_down", "keybind_lstick_down"},
    {"lstick_left", "keybind_lstick_left"},
    {"lstick_right", "keybind_lstick_right"},
    {"lstick_press", "keybind_lstick_press"},
    {"rstick_up", "keybind_rstick_up"},
    {"rstick_down", "keybind_rstick_down"},
    {"rstick_left", "keybind_rstick_left"},
    {"rstick_right", "keybind_rstick_right"},
    {"rstick_press", "keybind_rstick_press"},
    {"dpad_up", "keybind_dpad_up"},
    {"dpad_down", "keybind_dpad_down"},
    {"dpad_left", "keybind_dpad_left"},
    {"dpad_right", "keybind_dpad_right"},
    {"back", "keybind_back"},
    {"start", "keybind_start"},
}};

// The tokens of a bind the keyboard driver can match ("Return", "Shift+W"); the
// others are logged and left out.
std::string KnownKeys(std::string_view name, std::string_view bind) {
  std::string kept;
  while (!bind.empty()) {
    const size_t comma = bind.find(',');
    std::string_view token = bind.substr(0, comma);
    bind = comma == std::string_view::npos ? std::string_view() : bind.substr(comma + 1);
    while (token.starts_with(' '))
      token.remove_prefix(1);
    while (token.ends_with(' '))
      token.remove_suffix(1);
    std::string_view key = token;
    bool known = true;
    for (size_t plus = key.find('+'); plus != std::string_view::npos && plus > 0;
         plus = key.find('+')) {
      const std::string_view modifier = key.substr(0, plus);
      known &=
          modifier == "Shift" || modifier == "Ctrl" || modifier == "Control" || modifier == "Alt";
      key.remove_prefix(plus + 1);
    }
    if (known && rex::ui::ParseVirtualKey(key) != rex::ui::VirtualKey::kNone) {
      kept += kept.empty() ? "" : ",";
      kept += token;
    } else if (!token.empty()) {
      REXLOG_WARN("Controls: {} names a key the keyboard does not have: '{}'", name, token);
    }
  }
  return kept;
}

void LoadSet(const config::HostConfig& config, std::string_view prefix, BindingSet& set) {
  for (size_t i = 0; i < kNames.size(); ++i) {
    const std::string name = std::string(prefix) + kNames[i].file;
    if (auto value = config.Get(name)) {
      if (std::string keys = KnownKeys(name, *value); !keys.empty()) {
        set.keys[i] = std::move(keys);
      }
    }
  }
}

void LoadRate(const config::HostConfig& config, std::string_view name, double& rate) {
  const auto value = config.Get(name);
  if (!value)
    return;
  double parsed = 0.0;
  const auto [end, error] = std::from_chars(value->data(), value->data() + value->size(), parsed);
  if (error == std::errc() && end == value->data() + value->size() && parsed >= 0.0) {
    rate = parsed;
  }
}

}  // namespace

// Keys after Forza Horizon 6 on FM4's default controller layout (gamedb.slt,
// ControllerLayouts, LayoutId 0): A handbrake, B gear up, X gear down, Y rewind,
// LB clutch, RB look straight back, LT brake, RT throttle, left stick steering,
// right stick look (down = look back), right stick press heat and damage,
// D-pad up and down telemetry; BACK changes the camera, START pauses. Controls
// the layout leaves unused still get a key, so nothing is out of reach.
Controls DefaultControls() {
  Controls controls;
  BindingSet& menu = controls.menu;
  menu.keys[size_t(PadControl::kA)] = "Return,Space";
  menu.keys[size_t(PadControl::kB)] = "Escape,Backspace";
  menu.keys[size_t(PadControl::kX)] = "X";
  menu.keys[size_t(PadControl::kY)] = "Y";
  menu.keys[size_t(PadControl::kLeftShoulder)] = "Q";
  menu.keys[size_t(PadControl::kRightShoulder)] = "E";
  menu.keys[size_t(PadControl::kLeftTrigger)] = "Z";
  menu.keys[size_t(PadControl::kRightTrigger)] = "C";
  menu.keys[size_t(PadControl::kLStickUp)] = "W";
  menu.keys[size_t(PadControl::kLStickDown)] = "S";
  menu.keys[size_t(PadControl::kLStickLeft)] = "A";
  menu.keys[size_t(PadControl::kLStickRight)] = "D";
  menu.keys[size_t(PadControl::kLStickPress)] = "F";
  menu.keys[size_t(PadControl::kRStickUp)] = "I";
  menu.keys[size_t(PadControl::kRStickDown)] = "K";
  menu.keys[size_t(PadControl::kRStickLeft)] = "J";
  menu.keys[size_t(PadControl::kRStickRight)] = "L";
  menu.keys[size_t(PadControl::kRStickPress)] = "G";
  menu.keys[size_t(PadControl::kDpadUp)] = "Up";
  menu.keys[size_t(PadControl::kDpadDown)] = "Down";
  menu.keys[size_t(PadControl::kDpadLeft)] = "Left";
  menu.keys[size_t(PadControl::kDpadRight)] = "Right";
  menu.keys[size_t(PadControl::kBack)] = "Tab";
  menu.keys[size_t(PadControl::kStart)] = "P";

  BindingSet& driving = controls.driving;
  driving.keys[size_t(PadControl::kA)] = "Space";
  driving.keys[size_t(PadControl::kB)] = "E";
  driving.keys[size_t(PadControl::kX)] = "Q";
  driving.keys[size_t(PadControl::kY)] = "R";
  driving.keys[size_t(PadControl::kLeftShoulder)] = "Shift";
  driving.keys[size_t(PadControl::kRightShoulder)] = "C";
  driving.keys[size_t(PadControl::kLeftTrigger)] = "S";
  driving.keys[size_t(PadControl::kRightTrigger)] = "W";
  driving.keys[size_t(PadControl::kLStickUp)] = "Numpad8";
  driving.keys[size_t(PadControl::kLStickDown)] = "Numpad2";
  driving.keys[size_t(PadControl::kLStickLeft)] = "A";
  driving.keys[size_t(PadControl::kLStickRight)] = "D";
  driving.keys[size_t(PadControl::kLStickPress)] = "V";
  driving.keys[size_t(PadControl::kRStickUp)] = "Up";
  driving.keys[size_t(PadControl::kRStickDown)] = "Down";
  driving.keys[size_t(PadControl::kRStickLeft)] = "Left";
  driving.keys[size_t(PadControl::kRStickRight)] = "Right";
  driving.keys[size_t(PadControl::kRStickPress)] = "H";
  driving.keys[size_t(PadControl::kDpadUp)] = "T";
  driving.keys[size_t(PadControl::kDpadDown)] = "G";
  driving.keys[size_t(PadControl::kDpadLeft)] = "Comma";
  driving.keys[size_t(PadControl::kDpadRight)] = "Period";
  driving.keys[size_t(PadControl::kBack)] = "Tab";
  driving.keys[size_t(PadControl::kStart)] = "Escape";

  controls.ramp = {0.20, 0.12, 0.10, 0.08, 0.15};
  return controls;
}

Controls LoadControls(const config::HostConfig& config) {
  Controls controls = DefaultControls();
  LoadSet(config, "menu_", controls.menu);
  LoadSet(config, "driving_", controls.driving);
  RampRates& ramp = controls.ramp;
  LoadRate(config, "ramp_steer_seconds", ramp.steer_seconds);
  LoadRate(config, "ramp_steer_return_seconds", ramp.steer_return_seconds);
  LoadRate(config, "ramp_pedal_seconds", ramp.pedal_seconds);
  LoadRate(config, "ramp_pedal_return_seconds", ramp.pedal_return_seconds);
  LoadRate(config, "ramp_look_seconds", ramp.look_seconds);
  return controls;
}

void ApplyBindings(const BindingSet& set) {
  auto binds = std::make_shared<rex::input::mnk::KeybindMap>();
  for (size_t i = 0; i < kNames.size(); ++i) {
    binds->emplace(kNames[i].option, set.keys[i]);
  }
  rex::input::mnk::SetKeybindOverrides(std::move(binds));
}

}  // namespace fm4_static_recomp::input
