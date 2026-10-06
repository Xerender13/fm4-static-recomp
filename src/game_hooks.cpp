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

REXCVAR_DEFINE_DOUBLE(fm4_ui_speed, 1.0, "Forza Motorsport 4",
                      "Speed of the menus' animations against real time (1 = the console's)");

REXCVAR_DEFINE_STRING(fm4_physics_log, "", "Forza Motorsport 4",
                      "Diagnostics: a CSV file that receives every main loop time step, every "
                      "car's physics substep count and, ten times a simulated second, the "
                      "car's state");

namespace {
std::atomic<bool> g_race_session{false};
// The main loop's measured milliseconds since the showroom's and the UI's last update.
std::atomic<uint32_t> g_showroom_pending_ms{0};
std::atomic<uint32_t> g_ui_pending_ms{0};

// The guest vblank paces the main loop, which waits for one each tick, and D3D
// flips each swap on one. A race wakes on every vblank, so its frame rate is the
// vblank rate; menus see a fraction of 120 Hz (fm4_menu_vblank_hz).
constexpr char kVblankHzOption[] = "fh1_guest_vblank_hz";
constexpr double kMenuVblankHz = 120.0;
constexpr double kConsoleVblankHz = 60.0;
// A race load advances partly once per main loop tick, so the load runs on a
// fast vblank from the loading screen until the cars step (5.8 s at 120 Hz,
// 3.5 s at 480 Hz).
constexpr double kLoadVblankHz = 480.0;
std::atomic<bool> g_fast_load{false};
// A quit to the menus opens the loading screen while the race's cars still
// step, so only a car step after a tick without any ends a load.
std::atomic<bool> g_car_stepped{false};
std::atomic<bool> g_fast_load_cars_stopped{false};
std::mutex g_fast_load_mutex;
std::chrono::steady_clock::time_point g_fast_load_start;

void EndFastLoad(const char* reason, bool after_start_only = false) {
  if (!g_fast_load.load(std::memory_order_relaxed)) {
    return;
  }
  const std::lock_guard lock(g_fast_load_mutex);
  const auto elapsed = std::chrono::steady_clock::now() - g_fast_load_start;
  // The menu's last ticks can still run just after the loading screen opens.
  if (!g_fast_load.load(std::memory_order_relaxed) ||
      (after_start_only && elapsed < std::chrono::seconds(1))) {
    return;
  }
  g_fast_load.store(false, std::memory_order_relaxed);
  REXLOG_INFO("Race load: fast vblank off after {:.2f} s ({})",
              std::chrono::duration<double>(elapsed).count(), reason);
}

std::mutex g_physics_log_mutex;

std::FILE* PhysicsLog() {
  static std::FILE* const file = [] {
    const std::string path = REXCVAR_GET(fm4_physics_log);
    return path.empty() ? nullptr : std::fopen(path.c_str(), "w");
  }();
  return file;
}

void LogCarStep(uint32_t car, uint32_t substeps, uint32_t low_detail, double seconds) {
  g_car_stepped.store(true, std::memory_order_relaxed);
  if (g_fast_load_cars_stopped.load(std::memory_order_relaxed)) {
    EndFastLoad("first car step");
  }
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
  g_showroom_pending_ms.fetch_add(r30.u32, std::memory_order_relaxed);
  g_ui_pending_ms.fetch_add(r30.u32, std::memory_order_relaxed);
  if (!g_car_stepped.exchange(false, std::memory_order_relaxed) &&
      g_fast_load.load(std::memory_order_relaxed)) {
    g_fast_load_cars_stopped.store(true, std::memory_order_relaxed);
  }
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

void Fm4LoadingScreen() {
  const std::lock_guard lock(g_fast_load_mutex);
  if (g_fast_load.load(std::memory_order_relaxed)) {
    return;
  }
  g_fast_load_start = std::chrono::steady_clock::now();
  g_fast_load_cars_stopped.store(false, std::memory_order_relaxed);
  g_fast_load.store(true, std::memory_order_relaxed);
  REXLOG_INFO("Race load: fast vblank on");
}

void Fm4UiSceneStep(PPCRegister& f31) {
  // A load that ends in a menu instead of a race.
  EndFastLoad("menu", true);
  const uint32_t ms = g_ui_pending_ms.exchange(0, std::memory_order_relaxed);
  f31.f64 = std::min(ms, 100u) / 1000.0 * REXCVAR_GET(fm4_ui_speed);
}

void Fm4ShowroomCameraStep(PPCRegister& f1) {
  EndFastLoad("showroom", true);
  // At most the 1/60 s it replaces times six, so the first update after the
  // showroom opens does not jump the camera by the time spent elsewhere.
  const uint32_t ms = g_showroom_pending_ms.exchange(0, std::memory_order_relaxed);
  f1.f64 = std::min(ms, 100u) / 1000.0;
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
  const bool fast_load = g_fast_load.load(std::memory_order_relaxed);
  const bool race = g_race_session.load(std::memory_order_relaxed);
  const bool console = REXCVAR_GET(fm4_console_rate);
  const double vblank_hz = fast_load ? kLoadVblankHz
                           : console ? kConsoleVblankHz
                           : race    ? double(REXCVAR_GET(fm4_frame_rate))
                                     : kMenuVblankHz;
  static double applied_hz = 0.0;
  if (vblank_hz != applied_hz) {
    applied_hz = vblank_hz;
    rex::cvar::SetFlagByName(kVblankHzOption, std::to_string(vblank_hz));
  }
  const int32_t rate = REXCVAR_GET(fm4_menu_vblank_hz);
  if (fast_load || race || console || rate <= 0) {
    return false;
  }
  // Counting vblanks cannot lose one to timer jitter the way a time schedule
  // does when the two rates are equal.
  static uint32_t vblank_index = 0;
  const uint32_t divisor = std::max(1u, uint32_t(std::lround(kMenuVblankHz / rate)));
  return ++vblank_index % divisor != 0;
}
