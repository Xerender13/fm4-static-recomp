#pragma once

#include <cstdint>
#include <functional>
#include <mutex>
#include <optional>
#include <unordered_set>

#include "input/controls.h"

namespace fm4_static_recomp::input {

// Driving: the car is on track under the player's control. The pre-race
// screen, the pause menu, replays and the results are menus.
enum class InputContext { kMenu, kDriving };

// Fed by the game hooks: the race session flag and the race's menu scenes
// (pause, pre-race, post-race, replay) as they are created and destroyed, by
// address. Leaving a race forgets its scenes, so one torn down without its
// destructor being seen cannot hold the next race in the menu keys.
class InputContextState {
 public:
  void SetRaceSession(bool race);
  void RaceMenuSceneCreated(uint32_t scene);
  void RaceMenuSceneDestroyed(uint32_t scene);
  InputContext Current() const;

 private:
  mutable std::mutex mutex_;
  bool race_ = false;
  std::unordered_set<uint32_t> scenes_;
};

InputContextState& GameInputContext();
inline InputContext CurrentInputContext() {
  return GameInputContext().Current();
}

// Writes the context's binding set into the keyboard driver when the context
// changes.
class BindingSwitcher {
 public:
  explicit BindingSwitcher(Controls controls,
                           std::function<void(const BindingSet&)> apply = ApplyBindings);
  void Update(InputContext context);

 private:
  Controls controls_;
  std::function<void(const BindingSet&)> apply_;
  std::optional<InputContext> applied_;
};

}  // namespace fm4_static_recomp::input
