// Hooks the recompiled game calls (config/rexglue/analysis/main-xex.toml).

#include <algorithm>
#include <bit>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>

#include <rex/cvar.h>
#include <rex/ppc/context.h>
#include <rex/system/kernel_state.h>
#include <rex/system/xmemory.h>

#include "scripted_input.h"

REXCVAR_DEFINE_STRING(fm4_physics_log, "", "Forza Motorsport 4",
                      "Diagnostics: a CSV file that receives every main loop time step, every "
                      "car's physics substep count and, ten times a simulated second, the "
                      "car's state");

namespace {

std::mutex g_physics_log_mutex;

std::FILE* PhysicsLog() {
  static std::FILE* const file = [] {
    const std::string path = REXCVAR_GET(fm4_physics_log);
    return path.empty() ? nullptr : std::fopen(path.c_str(), "w");
  }();
  return file;
}

void LogCarStep(uint32_t car, uint32_t substeps, uint32_t low_detail, double seconds) {
  std::FILE* const log = PhysicsLog();
  if (!log) {
    return;
  }
  struct Clock {
    double time = 0, next_state = 0;
  };
  static std::unordered_map<uint32_t, Clock> clocks;
  const std::lock_guard lock(g_physics_log_mutex);
  Clock& clock = clocks[car];
  clock.time += seconds;
  std::fprintf(log, "c,%08X,%u,%u,%.6f,%.4f\n", car, low_detail, std::max(1u, substeps), seconds,
               clock.time);
  if (clock.time < clock.next_state) {
    return;
  }
  clock.next_state = clock.time + 0.1;
  const uint8_t* const state =
      rex::system::kernel_state()->memory()->virtual_membase() + car;
  std::fprintf(log, "s,%08X,%.4f", car, clock.time);
  for (uint32_t offset = 0; offset < 0x80; offset += 4) {
    uint32_t word;
    std::memcpy(&word, state + offset, sizeof(word));
    std::fprintf(log, ",%.5g", std::bit_cast<float>(std::byteswap(word)));
  }
  std::fputc('\n', log);
}
}  // namespace

void Fm4MainLoopStep(PPCRegister& r30, PPCRegister& f31) {
  if (std::FILE* const log = PhysicsLog()) {
    const std::lock_guard lock(g_physics_log_mutex);
    const auto now = std::chrono::steady_clock::now().time_since_epoch();
    std::fprintf(log, "f,%u,%.6f,%lld\n", r30.u32, f31.f64,
                 static_cast<long long>(
                     std::chrono::duration_cast<std::chrono::microseconds>(now).count()));
    std::fflush(log);
  }
}

void Fm4PhysicsLogCar(PPCRegister& r30, PPCRegister& r31, PPCRegister& r28, PPCRegister& f31) {
  LogCarStep(r30.u32, r31.u32, r28.u32 & 0xFF, f31.f64);
}

void Fm4PhysicsLogOtherCar(PPCRegister& r30, PPCRegister& r11, PPCRegister& f31) {
  LogCarStep(r30.u32, r11.u32, 2, f31.f64);
}

void Fm4AutovistaCameraUpdate() {
  // Runs every tick while Autovista is open, and nowhere else.
  static auto last = std::chrono::steady_clock::time_point{};
  const auto now = std::chrono::steady_clock::now();
  if (now - last > std::chrono::seconds(1)) {
    fm4_native::MarkInputEvent("autovista");
  }
  last = now;
}
