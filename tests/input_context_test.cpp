#include <catch2/catch_test_macros.hpp>

#include <string>
#include <vector>

#include <rex/input/mnk/mnk_bind.h>
#include <rex/ui/virtual_key.h>

#include "input/controls.h"
#include "input/input_context.h"

using namespace fm4_static_recomp::input;

TEST_CASE("The switcher applies a set only when the context changes", "[input_context]") {
  const Controls controls = DefaultControls();
  std::vector<BindingSet> applied;
  BindingSwitcher switcher(controls, [&](const BindingSet& set) { applied.push_back(set); });
  switcher.Update(InputContext::kMenu);
  switcher.Update(InputContext::kMenu);
  switcher.Update(InputContext::kDriving);
  REQUIRE(applied.size() == 2);
  CHECK(applied[0].keys == controls.menu.keys);
  CHECK(applied[1].keys == controls.driving.keys);
}

TEST_CASE("A key held across a switch releases with the key", "[input_context]") {
  const Controls controls = DefaultControls();
  BindingSet current;
  BindingSwitcher switcher(controls, [&](const BindingSet& set) { current = set; });
  bool key_down[256] = {};
  const auto pressed = [&] {
    std::vector<size_t> controls_down;
    for (size_t i = 0; i < current.keys.size(); ++i) {
      if (rex::input::mnk::BindPressed(key_down, current.keys[i], false)) {
        controls_down.push_back(i);
      }
    }
    return controls_down;
  };
  switcher.Update(InputContext::kDriving);
  key_down[static_cast<uint16_t>(rex::ui::VirtualKey::kW)] = true;
  CHECK(pressed() == std::vector<size_t>{size_t(PadControl::kRightTrigger)});
  // The pause menu opens with W still held: W moves the selection now.
  switcher.Update(InputContext::kMenu);
  CHECK(pressed() == std::vector<size_t>{size_t(PadControl::kLStickUp)});
  key_down[static_cast<uint16_t>(rex::ui::VirtualKey::kW)] = false;
  CHECK(pressed().empty());
}

TEST_CASE("Driving needs a race with no menu scene over it", "[input_context]") {
  InputContextState state;
  CHECK(state.Current() == InputContext::kMenu);
  state.SetRaceSession(true);
  CHECK(state.Current() == InputContext::kDriving);
  state.RaceMenuSceneCreated(0x100);
  CHECK(state.Current() == InputContext::kMenu);
  state.RaceMenuSceneDestroyed(0x100);
  CHECK(state.Current() == InputContext::kDriving);
  // A scene whose start the hooks did not see, or a second end, changes nothing.
  state.RaceMenuSceneDestroyed(0x100);
  state.RaceMenuSceneDestroyed(0x200);
  state.RaceMenuSceneCreated(0x300);
  CHECK(state.Current() == InputContext::kMenu);
  // Scenes created before the race flag (the pre-race screen) still count.
  InputContextState pre_race;
  pre_race.RaceMenuSceneCreated(0x100);
  pre_race.SetRaceSession(true);
  CHECK(pre_race.Current() == InputContext::kMenu);
}

TEST_CASE("Leaving a race forgets its menu scenes", "[input_context]") {
  InputContextState state;
  state.SetRaceSession(true);
  // Torn down without its destructor being seen (a quit, a restart).
  state.RaceMenuSceneCreated(0x100);
  state.SetRaceSession(false);
  state.SetRaceSession(true);
  CHECK(state.Current() == InputContext::kDriving);
}
