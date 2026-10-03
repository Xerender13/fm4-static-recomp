// Hooks the recompiled game calls (config/rexglue/analysis/main-xex.toml).

#include <chrono>

#include <rex/ppc/context.h>

#include "scripted_input.h"

void Fm4AutovistaCameraUpdate() {
  // Runs every tick while Autovista is open, and nowhere else.
  static auto last = std::chrono::steady_clock::time_point{};
  const auto now = std::chrono::steady_clock::now();
  if (now - last > std::chrono::seconds(1)) {
    fm4_native::MarkInputEvent("autovista");
  }
  last = now;
}
