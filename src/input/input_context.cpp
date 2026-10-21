#include "input/input_context.h"

#include <rex/logging.h>

namespace fm4_static_recomp::input {

void InputContextState::SetRaceSession(bool race) {
  const std::lock_guard lock(mutex_);
  if (race_ && !race) {
    scenes_.clear();
  }
  race_ = race;
}

void InputContextState::RaceMenuSceneCreated(uint32_t scene) {
  const std::lock_guard lock(mutex_);
  scenes_.insert(scene);
}

void InputContextState::RaceMenuSceneDestroyed(uint32_t scene) {
  const std::lock_guard lock(mutex_);
  scenes_.erase(scene);
}

InputContext InputContextState::Current() const {
  const std::lock_guard lock(mutex_);
  return race_ && scenes_.empty() ? InputContext::kDriving : InputContext::kMenu;
}

InputContextState& GameInputContext() {
  static InputContextState state;
  return state;
}

BindingSwitcher::BindingSwitcher(Controls controls, std::function<void(const BindingSet&)> apply)
    : controls_(std::move(controls)), apply_(std::move(apply)) {}

void BindingSwitcher::Update(InputContext context) {
  if (applied_ == context) {
    return;
  }
  applied_ = context;
  const bool driving = context == InputContext::kDriving;
  apply_(driving ? controls_.driving : controls_.menu);
  REXLOG_INFO("Input context: {}", driving ? "driving" : "menu");
}

}  // namespace fm4_static_recomp::input
