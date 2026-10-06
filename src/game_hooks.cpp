// Hooks the recompiled game calls (config/rexglue/analysis/main-xex.toml).

#include <algorithm>
#include <atomic>
#include <bit>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>

#include <rex/cvar.h>
#include <rex/logging.h>
#include <rex/ppc/context.h>
#include <rex/system/kernel_state.h>
#include <rex/system/xmemory.h>

#include "scripted_input.h"

REXCVAR_DEFINE_INT32(fm4_frame_rate, 120, "Forza Motorsport 4",
                     "Frame rate of races, 60 (the console's) to 240: the guest vblank rate "
                     "while a race session runs (fh1_guest_vblank_hz; 120 Hz outside races)")
    .range(60, 240);
REXCVAR_DEFINE_INT32(fm4_menu_vblank_hz, 120, "Forza Motorsport 4",
                     "Vblanks per second the game's main loop sees outside races: the nearest "
                     "whole fraction of the 120 Hz guest vblank there (0 = every vblank)");

REXCVAR_DEFINE_BOOL(fm4_console_rate, false, "Forza Motorsport 4",
                    "Diagnostics: the console's pacing, a 60 Hz guest vblank everywhere (races "
                    "at 60 fps, the showroom and other half-rate scenes at 30); overrides "
                    "fm4_frame_rate and fm4_menu_vblank_hz");

REXCVAR_DEFINE_BOOL(fm4_full_rate_scenes, true, "Forza Motorsport 4",
                    "Render the scenes the game draws at half the frame rate (showroom, "
                    "pre-race screen, Autovista) at the full rate; false is the game's own "
                    "half rate");

REXCVAR_DEFINE_STRING(fm4_physics_log, "", "Forza Motorsport 4",
                      "Diagnostics: a CSV file that receives every main loop time step, every "
                      "car's physics substep count and, ten times a simulated second, the "
                      "car's state");

namespace {
std::atomic<bool> g_race_session{false};

// The guest vblank paces the main loop, which waits for one each tick, and D3D
// flips each swap on one. A race wakes on every vblank, so its frame rate is the
// vblank rate; menus see a fraction of 120 Hz (fm4_menu_vblank_hz).
constexpr char kVblankHzOption[] = "fh1_guest_vblank_hz";
constexpr double kMenuVblankHz = 120.0;
constexpr double kConsoleVblankHz = 60.0;

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

void Fm4PresentInterval(PPCRegister& r4) {
  // Set every frame in races and on the pre-race screen.
  static uint32_t last = 0;
  if (r4.u32 != last) {
    last = r4.u32;
    REXLOG_INFO("Present interval: {}", last);
  }
  if (r4.u32 > 1 && REXCVAR_GET(fm4_full_rate_scenes)) {
    r4.u64 = 1;
  }
}

void Fm4MainLoopSession(PPCRegister& r11) {
  const bool race = r11.u32 != 0;
  g_race_session.store(race, std::memory_order_relaxed);
}

bool Fm4SkipVblankCallback() {
  // Runs on every vblank, so a new rate applies from the next one. A race waking
  // on every second vblank of one at twice its rate is no substitute: a frame then
  // flips on whichever vblank follows its swap, and frames whose swap falls near
  // the one in between alternate between half and one and a half frame periods.
  const bool race = g_race_session.load(std::memory_order_relaxed);
  const bool console = REXCVAR_GET(fm4_console_rate);
  const double vblank_hz = console ? kConsoleVblankHz
                           : race    ? double(REXCVAR_GET(fm4_frame_rate))
                                     : kMenuVblankHz;
  static double applied_hz = 0.0;
  if (vblank_hz != applied_hz) {
    applied_hz = vblank_hz;
    rex::cvar::SetFlagByName(kVblankHzOption, std::to_string(vblank_hz));
  }
  const int32_t rate = REXCVAR_GET(fm4_menu_vblank_hz);
  if (race || console || rate <= 0) {
    return false;
  }
  // Counting vblanks cannot lose one to timer jitter the way a time schedule
  // does when the two rates are equal.
  static uint32_t vblank_index = 0;
  const uint32_t divisor = std::max(1u, uint32_t(std::lround(kMenuVblankHz / rate)));
  return ++vblank_index % divisor != 0;
}
