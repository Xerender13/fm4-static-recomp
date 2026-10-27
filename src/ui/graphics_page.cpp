#include "ui/graphics_page.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <iterator>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include <rex/cvar.h>
#include <rex/logging.h>
#include <rex/ui/upscaler.h>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

namespace fm4_static_recomp::ui {
namespace {

using hostui::MenuRow;
using hostui::MenuScreen;

struct Choice {
  const char* label;
  const char* value;
};

struct Setting {
  const char* name;
  bool string;  // written quoted
  std::vector<Choice> choices;  // none for a whole number in [low, high]
  int low = 0;
  int high = 0;
};

const Setting kFrameRate = {"fm4_frame_rate", false, {}, 60, 240};
const Setting kVsync = {"vulkan_present_mode", true, {{"OFF", "immediate"}, {"ON", "fifo"}}};
const Setting kDetail = {"fm4_level_of_detail",
                         true,
                         {{"CONSOLE", "console"},
                          {"HIGH", "high"},
                          {"ULTRA", "ultra"}}};
const Setting kScale = {"resolution_scale", false, {{"1X", "1"}, {"2X", "2"}, {"3X", "3"}, {"4X", "4"}}};
const Setting kUpscaler = {"present_effect",
                           true,
                           {{"OFF", "bilinear"}, {"FSR 2", "fsr2"}, {"FSR 3", "fsr3"}, {"DLSS", "dlss"}}};
const Setting kDlssPreset = {
    "present_dlss_preset",
    true,
    {{"AUTO", "auto"}, {"K", "k"}, {"M", "m"}, {"J", "j"}, {"L", "l"}, {"DEFAULT", "default"}}};
const Setting kSharpness = {"present_upscaler_sharpness",
                            false,
                            {{"OFF", "0"},
                             {"10 %", "10"},
                             {"20 %", "20"},
                             {"30 %", "30"},
                             {"40 %", "40"},
                             {"50 %", "50"},
                             {"60 %", "60"},
                             {"70 %", "70"},
                             {"80 %", "80"},
                             {"90 %", "90"},
                             {"100 %", "100"}}};
const Setting kPostAa = {
    "swap_post_effect", true, {{"OFF", "none"}, {"FXAA", "fxaa"}, {"FXAA EXTREME", "fxaa_extreme"}}};
const Setting kShowroomMsaa = {
    "fm4_showroom_msaa", true, {{"4X", "4x"}, {"2X", "2x"}, {"OFF", "off"}}};
const Setting kRaceMsaa = {"fm4_race_msaa", true, {{"4X", "4x"}, {"2X", "2x"}, {"OFF", "off"}}};
const Setting kAnisotropy = {"anisotropic_override",
                             false,
                             {{"GAME", "-1"}, {"2X", "2"}, {"4X", "3"}, {"8X", "4"}, {"16X", "5"}}};

const Setting kMotionBlur = {"fm4_road_detail_blur", false, {{"ON", "true"}, {"OFF", "false"}}};
const Setting kDepthOfField = {"fm4_depth_of_field", false, {{"ON", "true"}, {"OFF", "false"}}};
const Setting kCarDirt = {"fm4_car_dirt", false, {{"ON", "true"}, {"OFF", "false"}}};

const Setting kSceneryShadows = {"fm4_scenery_shadows", false, {{"OFF", "false"}, {"ON", "true"}}};
const Setting kReflectionUpdates = {
    "fm4_reflection_updates",
    true,
    {{"CONSOLE", "console"}, {"EVERY RACE", "every_race"}, {"DOUBLE", "double"}}};
const Setting kMirror = {"fm4_full_rate_mirror", false, {{"CONSOLE", "false"}, {"FULL", "true"}}};
const Setting kBloom = {"fm4_full_rate_bloom", false, {{"CONSOLE", "false"}, {"FULL RATE", "true"}}};
const Setting kSoftParticles = {"fm4_soft_particles", false, {{"OFF", "false"}, {"ON", "true"}}};
const Setting kCrowds = {
    "fm4_crowds_every_race", false, {{"CONSOLE", "false"}, {"EVERY RACE", "true"}}};

// Every setting but the frame rate, the resolution scale and the upscaler's.
const Setting* const kPresetSettings[] = {
    &kPostAa, &kShowroomMsaa, &kRaceMsaa, &kAnisotropy, &kSceneryShadows, &kReflectionUpdates,
    &kDetail, &kMirror, &kBloom, &kSoftParticles, &kCrowds, &kMotionBlur, &kDepthOfField,
    &kCarDirt};
constexpr size_t kPresetSettingCount = std::size(kPresetSettings);

const Setting* const kSettings[] = {
    &kFrameRate, &kVsync, &kScale, &kUpscaler, &kDlssPreset, &kSharpness, &kPostAa,
    &kShowroomMsaa, &kRaceMsaa, &kAnisotropy,
    &kSceneryShadows, &kReflectionUpdates, &kDetail, &kMirror, &kBloom, &kSoftParticles, &kCrowds,
    &kMotionBlur, &kDepthOfField, &kCarDirt};

struct Preset {
  const char* label;
  std::array<const char*, kPresetSettingCount> values;  // in kPresetSettings' order
};

// The page shows the preset all settings match, or CUSTOM.
const Preset kPresets[] = {
    {"CONSOLE",
     {"none", "4x", "2x", "-1", "false", "console", "console", "false", "false", "false",
      "false", "true", "true", "true"}},
    {"HIGH",
     {"fxaa", "2x", "2x", "5", "false", "console", "high", "false", "true", "false",
      "false", "true", "true", "true"}},
    {"ULTRA",
     {"fxaa_extreme", "2x", "2x", "5", "false", "every_race", "ultra", "true", "true",
      "true", "false", "true", "true", "true"}},
    {"EXPERIMENTAL",
     {"fxaa_extreme", "4x", "4x", "5", "true", "double", "ultra", "true", "true", "true",
      "true", "true", "true", "true"}},
};
constexpr int kPresetCount = int(std::size(kPresets));

struct RowText {
  const char* label;
  const Setting* setting;  // null for the preset row
  const char* description;
  bool divider_above = false;
  // The launcher shows the row only while this setting has this value.
  const Setting* shown_with = nullptr;
  const char* shown_value = nullptr;
};

// The page's rows in order; the frame rate, scale, upscaler and preset rows
// add live behaviour of their own in CreateGraphicsPage. The DLSS preset is
// the launcher's alone: DLSS takes it only as it starts.
const RowText kRows[] = {
    {"FRAME RATE", &kFrameRate,
     "Frames per second in races, menus and the showroom, from the console's 60 up to your "
     "display's refresh rate. A rate the GPU cannot hold stutters: lower it or the resolution "
     "scale."},
    {"VSYNC", &kVsync,
     "On stops tearing on a display without G-Sync or FreeSync. With either, leave it off: "
     "on, an Nvidia GPU shows frames through the desktop, without variable refresh and at a "
     "lower frame rate."},
    {"RESOLUTION SCALE", &kScale,
     "Renders the game at a multiple of 1280x720: sharper edges and textures, more GPU load."},
    {"UPSCALER", &kUpscaler,
     "Upscales the picture from the resolution scale to your display with the game's depth and "
     "motion, and smooths edges on the way: DLSS on NVIDIA RTX GPUs, FSR 2 and FSR 3 on any. "
     "Off stretches the picture. While one runs, MSAA and post AA are off; the MSAA change "
     "applies from the next screen."},
    {"DLSS PRESET", &kDlssPreset,
     "The DLSS model. Auto takes M where DLSS upscales by more than 1.8x on an RTX 40 or later "
     "GPU, else K; Default leaves the choice to the DLSS runtime and the NVIDIA app's override.",
     false, &kUpscaler, "dlss"},
    {"SHARPNESS", &kSharpness,
     "Sharpens the picture after DLSS, FSR 2 or FSR 3. Off leaves the upscaler's picture as it "
     "is."},
    {"GRAPHICS PRESET", nullptr,
     "Sets every setting below. Console is the game as on the console; "
     "High raises most, adds FXAA and uses 2x MSAA everywhere; Ultra also raises the level of "
     "detail, the mirror and particles and uses FXAA Extreme; Experimental adds 4x MSAA "
     "everywhere, scenery shadows, double reflection updates and crowds in every race. A "
     "change to one setting makes the preset Custom."},
    {"SHOWROOM MSAA", &kShowroomMsaa,
     "The game's multisampling in the showroom, menus, pre-race, replays and photo mode. 4x is "
     "the console's and draws the scene in three tiles, 2x in two, Off in one: fewer samples "
     "are faster and leave edges to the resolution scale. Applies from the next screen. Off "
     "while DLSS or FSR 2 / 3 upscales: the upscaler smooths edges itself.",
     true},
    {"RACE MSAA", &kRaceMsaa,
     "The game's multisampling in races, the countdown and the results. 2x is the console's "
     "(4x in races alone on track); 4x has twice the samples and three tiles instead of two, "
     "heavy on the GPU; Off draws one tile and leaves edges to the resolution scale. Applies "
     "from the next race. Off while DLSS or FSR 2 / 3 upscales."},
    {"ANISOTROPIC FILTERING", &kAnisotropy,
     "Keeps textures sharp at grazing angles. Game leaves each texture the filtering the game "
     "asks for."},
    {"LEVEL OF DETAIL", &kDetail,
     "How far cars keep their detailed models. Ultra also keeps the best model loaded for your "
     "car, at more guest memory per car. Applies from the next race."},
    {"SCENERY SHADOWS", &kSceneryShadows,
     "Experimental, so in no other preset: shadows of the cones, barriers and other scenery in "
     "races, which the game draws only in replays and photo mode. Known bug: a shadow vanishes "
     "or flickers as its object nears the edge of the screen, most visibly on large objects."},
    {"REFLECTION UPDATES", &kReflectionUpdates,
     "How often car reflections are redrawn. The console leaves them frozen in 16-car races "
     "and split screen; Every Race redraws them there too; Double redraws twice as many "
     "faces each frame."},
    {"MIRROR", &kMirror,
     "Full draws the mirror every frame with up to 16 cars and the cones and barriers, where "
     "the console halves its rate and draws 6 cars (12- and 16-car races)."},
    {"SOFT PARTICLES", &kSoftParticles,
     "Smoke and dust fade where they meet the ground and cars instead of cutting into them, "
     "as the game does in replays and photo mode."},
    {"CROWDS", &kCrowds,
     "Every Race adds the crowds the console leaves out of 1- and 16-car races. Uses more "
     "guest memory."},
    {"POST AA", &kPostAa,
     "Smooths edges in the finished picture. FXAA Extreme smooths more and softens more. Off "
     "while DLSS or FSR 2 / 3 upscales: the upscaler smooths edges itself.",
     true},
    {"BLOOM", &kBloom,
     "Full Rate updates the glow every frame, where the console skips every second one "
     "(16-car races)."},
    {"MOTION BLUR", &kMotionBlur,
     "Blurs the road surface with speed. Off keeps the asphalt as sharp as when standing "
     "still."},
    {"DEPTH OF FIELD", &kDepthOfField,
     "Blurs what is out of focus in the showroom, pre-race and replay cameras and photo mode. "
     "Applies from the next screen."},
    {"CAR DIRT", &kCarDirt,
     "The dirt cars gather in a race. Off leaves cars clean."},
};

const RowText& Text(const char* label) {
  for (const RowText& row : kRows) {
    if (std::string_view(row.label) == label) {
      return row;
    }
  }
  std::abort();
}

std::string JsonString(std::string_view text) {
  std::string out = "\"";
  for (const char c : text) {
    switch (c) {
      case '"':
        out += "\\\"";
        break;
      case '\\':
        out += "\\\\";
        break;
      case '\n':
        out += "\\n";
        break;
      default:
        out += c;
    }
  }
  return out + "\"";
}

std::string EffectiveDefault(const Setting& setting) {
  // A built-in default (built_in_defaults.cpp), over the SDK's auto.
  if (&setting == &kVsync) {
    return "immediate";
  }
  const rex::cvar::FlagEntry* info = rex::cvar::GetFlagInfo(setting.name);
  return info ? info->default_value : std::string();
}

std::string RowJson(const RowText& row) {
  std::string out = "{\"label\":" + JsonString(row.label);
  if (!row.setting) {
    return out + ",\"kind\":\"preset\",\"description\":" + JsonString(row.description) + "}";
  }
  const Setting& setting = *row.setting;
  out += ",\"key\":" + JsonString(setting.name);
  out += std::string(",\"string\":") + (setting.string ? "true" : "false");
  if (setting.choices.empty()) {
    out += ",\"kind\":\"range\",\"low\":" + std::to_string(setting.low) +
           ",\"high\":" + std::to_string(setting.high);
  } else {
    out += ",\"kind\":\"choice\",\"choices\":[";
    for (size_t i = 0; i < setting.choices.size(); ++i) {
      out += std::string(i ? "," : "") + "{\"label\":" + JsonString(setting.choices[i].label) +
             ",\"value\":" + JsonString(setting.choices[i].value) + "}";
    }
    out += "]";
  }
  out += ",\"default\":" + JsonString(EffectiveDefault(setting));
  if (row.shown_with) {
    out += ",\"shown_when\":{" + JsonString(row.shown_with->name) + ":" +
           JsonString(row.shown_value) + "}";
  }
  return out + ",\"description\":" + JsonString(row.description) + "}";
}

// Each scale switch rebuilds the renderer for most of a second, so steps
// through the scales apply once the choice has rested this long.
constexpr auto kScaleSettle = std::chrono::milliseconds(500);
// The frame rate applies at once, but a slide passes through many values; the
// file is written once the value has rested this long.
constexpr auto kFrameRateSettle = std::chrono::milliseconds(500);

std::string Live(const char* name) { return rex::cvar::GetFlagByName(name); }

int LiveNumber(const char* name) { return std::atoi(Live(name).c_str()); }

// The highest frame rate the page offers: the display's refresh rate, within
// the setting's range.
int FrameRateCeiling(uint32_t refresh_hz) {
  return refresh_hz ? std::clamp(int(refresh_hz), kFrameRate.low, kFrameRate.high)
                    : kFrameRate.high;
}

int Find(const Setting& setting, std::string_view value) {
  if (setting.choices.empty()) {
    const int number = std::atoi(std::string(value).c_str());
    return std::to_string(number) == value && number >= setting.low && number <= setting.high
               ? 0
               : -1;
  }
  for (size_t i = 0; i < setting.choices.size(); ++i) {
    if (value == setting.choices[i].value) {
      return int(i);
    }
  }
  return -1;
}

int Step(const Setting& setting, int current, int direction) {
  const int count = int(setting.choices.size());
  return current < 0 ? 0 : (current + direction + count) % count;
}

std::string Label(const Setting& setting, int index) {
  return index >= 0 ? std::string(setting.choices[size_t(index)].label) : std::string("CUSTOM");
}

bool FromCommandLine(const char* name) {
  return rex::cvar::GetFlagSource(name) == rex::cvar::Source::kCommandLine;
}

// The per-axis scales override resolution_scale when set.
bool SetByLaunchOption(const Setting& setting) {
  if (&setting == &kScale) {
    return FromCommandLine(kScale.name) || FromCommandLine("draw_resolution_scale_x") ||
           FromCommandLine("draw_resolution_scale_y");
  }
  return FromCommandLine(setting.name);
}

uint32_t LiveScale(const GraphicsPageServices& services) {
  uint32_t scale = services.draw_resolution_scale ? services.draw_resolution_scale() : 0;
  if (!scale) {
    scale = uint32_t(std::max(1, std::atoi(Live(kScale.name).c_str())));
  }
  return scale;
}

// The upscaler present_effect names, while one is upscaling.
std::string TemporalUpscalerName() {
  if (!rex::ui::IsTemporalUpscalerActive()) {
    return std::string();
  }
  const std::string effect = Live("present_effect");
  return effect == "dlss" ? "DLSS" : effect == "fsr3" ? "FSR 3" : "FSR 2";
}

std::string ResolutionLine(const GraphicsPageServices& services) {
  const uint32_t scale = LiveScale(services);
  const uint32_t render_width = 1280 * scale;
  const uint32_t render_height = 720 * scale;
  const auto size = [](uint32_t width, uint32_t height) {
    return std::to_string(width) + "x" + std::to_string(height);
  };
  std::string line = "Renders " + size(render_width, render_height);
  const auto output = services.output_size ? services.output_size() : std::nullopt;
  if (!output || !output->first || !output->second) {
    return line;
  }
  const auto [output_width, output_height] = *output;
  const std::string upscaler = TemporalUpscalerName();
  if (output_width == render_width && output_height == render_height) {
    // At 1:1 the upscalers only anti-alias, under the names NVIDIA and AMD give that mode.
    if (upscaler.empty()) {
      return line + ", shown at that size.";
    }
    return line + ", shown at that size with " + (upscaler == "DLSS" ? "DLAA" : upscaler + " Native AA") + ".";
  }
  if (output_width * output_height > render_width * render_height) {
    if (upscaler.empty()) {
      return line + ", stretched to " + size(output_width, output_height) + ".";
    }
    char factor[16];
    std::snprintf(factor, sizeof(factor), "%.1f", double(output_width) / double(render_width));
    return line + ", upscaled to " + size(output_width, output_height) + " by " + upscaler + " (" +
           factor + "x).";
  }
  return line + ", downscaled to " + size(output_width, output_height) + ".";
}

bool Set(const Setting& setting, const char* value, config::HostConfig& config) {
  if (!rex::cvar::SetFlagByName(setting.name, value)) {
    REXLOG_ERROR("Graphics page: {} refused {}", setting.name, value);
    return false;
  }
  REXLOG_INFO("Graphics page: {} = {}", setting.name, value);
  if (&setting == &kScale) {
    // The per-axis scales win over resolution_scale while either is set.
    for (const char* axis : {"draw_resolution_scale_x", "draw_resolution_scale_y"}) {
      if (rex::cvar::HasNonDefaultValue(axis)) {
        rex::cvar::SetFlagByName(axis, value);
      }
    }
  }
  config.Set(setting.name, setting.string ? config::Quote(value) : std::string(value));
  return true;
}

void Save(config::HostConfig& config, const std::function<void()>& saved) {
  if (!config.Save()) {
    REXLOG_ERROR("Graphics page: could not write {}", config.path().string());
  } else if (saved) {
    saved();
  }
}

void Commit(const Setting& setting, const char* value, config::HostConfig& config,
            const std::function<void()>& saved) {
  if (Set(setting, value, config)) {
    Save(config, saved);
  }
}

int LivePreset() {
  for (int i = 0; i < kPresetCount; ++i) {
    bool match = true;
    for (size_t j = 0; j < kPresetSettingCount && match; ++j) {
      match = Live(kPresetSettings[j]->name) == kPresets[i].values[j];
    }
    if (match) {
      return i;
    }
  }
  return -1;
}

MenuRow Row(const char* label, const Setting& setting, config::HostConfig& config,
            const GraphicsPageServices& services) {
  MenuRow row;
  row.label = label;
  row.description = Text(label).description;
  row.divider_above = Text(label).divider_above;
  row.value = [&setting] { return Label(setting, Find(setting, Live(setting.name))); };
  // A launch option sets the starting value only; the badge goes once the
  // page changes the setting.
  row.badge = [&setting] {
    return SetByLaunchOption(setting) ? std::string("Set by a launch option for this session.")
                                      : std::string();
  };
  row.adjust = [&setting, &config, saved = services.saved](int direction) {
    const int next = Step(setting, Find(setting, Live(setting.name)), direction);
    Commit(setting, setting.choices[size_t(next)].value, config, saved);
  };
  return row;
}

// A temporal upscaler takes over the anti-aliasing (TilingChoice in
// game_hooks.cpp, the post effect in the SDK): the row says so and keeps the
// player's choice for later.
MenuRow AntiAliasingRow(const char* label, const Setting& setting, config::HostConfig& config,
                        const GraphicsPageServices& services) {
  MenuRow row = Row(label, setting, config, services);
  row.value = [&setting] {
    const std::string upscaler = TemporalUpscalerName();
    return upscaler.empty() ? Label(setting, Find(setting, Live(setting.name)))
                            : "OFF (" + upscaler + ")";
  };
  row.enabled = [] { return !rex::ui::IsTemporalUpscalerActive(); };
  return row;
}

// Why the upscaler `value` names cannot run on this GPU, or nullopt when it can.
std::optional<std::string> UpscalerProblem(const GraphicsPageServices& services,
                                           std::string_view value) {
  if (value == "bilinear" || !services.upscalers) {
    return std::nullopt;
  }
  for (const rex::ui::UpscalerInfo& info : services.upscalers()) {
    if (info.id == value) {
      return info.available ? std::nullopt : std::optional(info.unavailable_reason);
    }
  }
  return std::string("its runtime is missing");
}

// Steps over the upscalers this GPU cannot run; OFF always can.
MenuRow UpscalerRow(config::HostConfig& config, const GraphicsPageServices& services) {
  MenuRow row = Row("UPSCALER", kUpscaler, config, services);
  row.adjust = [&config, services](int direction) {
    int index = Find(kUpscaler, Live(kUpscaler.name));
    do {
      index = Step(kUpscaler, index, direction);
    } while (UpscalerProblem(services, kUpscaler.choices[size_t(index)].value));
    Commit(kUpscaler, kUpscaler.choices[size_t(index)].value, config, services.saved);
  };
  row.note = [services] {
    if (rex::ui::IsTemporalUpscalerActive()) {
      return ResolutionLine(services);
    }
    const int index = Find(kUpscaler, Live(kUpscaler.name));
    const auto problem =
        index > 0 ? UpscalerProblem(services, kUpscaler.choices[size_t(index)].value)
                  : std::nullopt;
    return problem ? Label(kUpscaler, index) + " does not run here: " + *problem + "."
                   : std::string();
  };
  return row;
}

}  // namespace

bool WriteGraphicsSchema(const std::filesystem::path& path) {
  std::string out = "{\"schema_version\":1,\"blocks\":[{\"rows\":[";
  bool first = true;
  for (const RowText& row : kRows) {
    if (row.divider_above) {
      out += "]},{\"rows\":[";
      first = true;
    }
    out += (first ? "" : ",") + RowJson(row);
    first = false;
  }
  out += "]}],\"presets\":[";
  for (int i = 0; i < kPresetCount; ++i) {
    out += std::string(i ? "," : "") + "{\"label\":" + JsonString(kPresets[i].label) +
           ",\"values\":{";
    for (size_t j = 0; j < kPresetSettingCount; ++j) {
      out += std::string(j ? "," : "") + JsonString(kPresetSettings[j]->name) + ":" +
             JsonString(kPresets[i].values[j]);
    }
    out += "}}";
  }
  out += "]}\n";
  std::ofstream file(path, std::ios::binary | std::ios::trunc);
  file << out;
  return bool(file);
}

void ApplySavedGraphicsSettings(const config::HostConfig& config) {
  for (const Setting* setting : kSettings) {
    const auto value = config.Get(setting->name);
    if (!value) {
      continue;
    }
    if (SetByLaunchOption(*setting)) {
      REXLOG_INFO("Graphics settings: {} comes from the command line, not {}", setting->name,
                  config.path().string());
    } else if (Find(*setting, *value) < 0 || !rex::cvar::SetFlagByName(setting->name, *value)) {
      REXLOG_WARN("Graphics settings: ignored {} = {} in {}", setting->name, *value,
                  config.path().string());
    } else {
      REXLOG_INFO("Graphics settings: {} = {}", setting->name, *value);
    }
  }
}

uint32_t DisplayRefreshHz(void* native_window) {
#if defined(_WIN32)
  MONITORINFOEXW monitor = {};
  monitor.cbSize = sizeof(monitor);
  DEVMODEW mode = {};
  mode.dmSize = sizeof(mode);
  if (native_window &&
      GetMonitorInfoW(MonitorFromWindow(HWND(native_window), MONITOR_DEFAULTTONEAREST),
                      &monitor) &&
      EnumDisplaySettingsW(monitor.szDevice, ENUM_CURRENT_SETTINGS, &mode) &&
      mode.dmDisplayFrequency > 1) {
    return mode.dmDisplayFrequency;
  }
#endif
  return 0;
}

void LimitFrameRateToDisplay(uint32_t refresh_hz) {
  const int ceiling = FrameRateCeiling(refresh_hz);
  if (!refresh_hz || SetByLaunchOption(kFrameRate) || LiveNumber(kFrameRate.name) <= ceiling) {
    return;
  }
  rex::cvar::SetFlagByName(kFrameRate.name, std::to_string(ceiling));
  REXLOG_INFO("Graphics settings: {} lowered to {} for a {} Hz display", kFrameRate.name, ceiling,
              refresh_hz);
}

std::unique_ptr<MenuScreen> CreateGraphicsPage(config::HostConfig& config,
                                               GraphicsPageServices services) {
  std::vector<MenuRow> rows;
  // The frame rate applies live with every step; the file is written once it
  // has rested for kFrameRateSettle, or when the page closes.
  struct PendingFrameRate {
    bool unsaved = false;
    std::chrono::steady_clock::time_point changed_at;
  };
  auto pending_rate = std::make_shared<PendingFrameRate>();
  MenuRow frame_rate;
  frame_rate.label = "FRAME RATE";
  frame_rate.slider = hostui::MenuSlider{
      .range =
          [services] {
            return std::pair{kFrameRate.low, FrameRateCeiling(services.display_refresh_hz
                                                                  ? services.display_refresh_hz()
                                                                  : 0)};
          },
      .get = [] { return LiveNumber(kFrameRate.name); },
      .set =
          [pending_rate](int value) {
            if (rex::cvar::SetFlagByName(kFrameRate.name, std::to_string(value))) {
              pending_rate->unsaved = true;
              pending_rate->changed_at = std::chrono::steady_clock::now();
            }
          }};
  frame_rate.badge = [] {
    return SetByLaunchOption(kFrameRate) ? std::string("Set by a launch option for this session.")
                                         : std::string();
  };
  frame_rate.description = Text("FRAME RATE").description;
  frame_rate.note = [services] {
    const uint32_t hz = services.display_refresh_hz ? services.display_refresh_hz() : 0;
    return hz ? "Your display refreshes at " + std::to_string(hz) + " Hz." : std::string();
  };
  rows.push_back(std::move(frame_rate));

  MenuRow vsync = Row("VSYNC", kVsync, config, services);
  rows.push_back(std::move(vsync));

  // A chosen scale applies once it has rested for kScaleSettle, or when the
  // page closes.
  struct PendingScale {
    int index = -1;
    std::chrono::steady_clock::time_point chosen_at;
  };
  auto pending = std::make_shared<PendingScale>();
  MenuRow scale = Row("RESOLUTION SCALE", kScale, config, services);
  scale.value = [pending] {
    return Label(kScale, pending->index >= 0 ? pending->index : Find(kScale, Live(kScale.name)));
  };
  scale.adjust = [pending](int direction) {
    const int current = pending->index >= 0 ? pending->index : Find(kScale, Live(kScale.name));
    pending->index = Step(kScale, current, direction);
    pending->chosen_at = std::chrono::steady_clock::now();
  };
  scale.note = [services] { return ResolutionLine(services); };
  // The renderer switches between frames; until it has, the change waits.
  scale.pending = [services, pending] {
    return pending->index >= 0 ||
           (services.draw_resolution_scale &&
            std::to_string(services.draw_resolution_scale()) != Live(kScale.name));
  };
  rows.push_back(std::move(scale));

  rows.push_back(UpscalerRow(config, services));

  MenuRow sharpness = Row("SHARPNESS", kSharpness, config, services);
  sharpness.enabled = [] { return rex::ui::IsTemporalUpscalerActive(); };
  rows.push_back(std::move(sharpness));

  MenuRow preset;
  preset.label = "GRAPHICS PRESET";
  preset.value = [] {
    const int index = LivePreset();
    return std::string(index >= 0 ? kPresets[index].label : "CUSTOM");
  };
  preset.adjust = [&config, saved = services.saved](int direction) {
    const int current = LivePreset();
    const int next = current < 0 ? (direction > 0 ? 0 : kPresetCount - 1)
                                 : (current + direction + kPresetCount) % kPresetCount;
    for (size_t j = 0; j < kPresetSettingCount; ++j) {
      Set(*kPresetSettings[j], kPresets[next].values[j], config);
    }
    Save(config, saved);
  };
  preset.description = Text("GRAPHICS PRESET").description;
  rows.push_back(std::move(preset));

  MenuRow showroom_msaa = AntiAliasingRow("SHOWROOM MSAA", kShowroomMsaa, config, services);
  rows.push_back(std::move(showroom_msaa));

  MenuRow race_msaa = AntiAliasingRow("RACE MSAA", kRaceMsaa, config, services);
  rows.push_back(std::move(race_msaa));

  MenuRow anisotropy = Row("ANISOTROPIC FILTERING", kAnisotropy, config, services);
  rows.push_back(std::move(anisotropy));

  MenuRow detail = Row("LEVEL OF DETAIL", kDetail, config, services);
  rows.push_back(std::move(detail));

  MenuRow scenery_shadows = Row("SCENERY SHADOWS", kSceneryShadows, config, services);
  rows.push_back(std::move(scenery_shadows));

  MenuRow reflection_updates = Row("REFLECTION UPDATES", kReflectionUpdates, config, services);
  rows.push_back(std::move(reflection_updates));

  MenuRow mirror = Row("MIRROR", kMirror, config, services);
  rows.push_back(std::move(mirror));

  MenuRow soft_particles = Row("SOFT PARTICLES", kSoftParticles, config, services);
  rows.push_back(std::move(soft_particles));

  MenuRow crowds = Row("CROWDS", kCrowds, config, services);
  rows.push_back(std::move(crowds));

  MenuRow post_aa = AntiAliasingRow("POST AA", kPostAa, config, services);
  rows.push_back(std::move(post_aa));

  MenuRow bloom = Row("BLOOM", kBloom, config, services);
  rows.push_back(std::move(bloom));

  MenuRow motion_blur = Row("MOTION BLUR", kMotionBlur, config, services);
  rows.push_back(std::move(motion_blur));

  MenuRow depth_of_field = Row("DEPTH OF FIELD", kDepthOfField, config, services);
  rows.push_back(std::move(depth_of_field));

  MenuRow car_dirt = Row("CAR DIRT", kCarDirt, config, services);
  rows.push_back(std::move(car_dirt));

  auto screen = std::make_unique<MenuScreen>("GRAPHICS", std::move(rows));
  screen->set_update([pending, pending_rate, &config, saved = services.saved](bool closing) {
    const auto now = std::chrono::steady_clock::now();
    if (pending->index >= 0 && (closing || now - pending->chosen_at >= kScaleSettle)) {
      Commit(kScale, kScale.choices[size_t(std::exchange(pending->index, -1))].value, config,
             saved);
    }
    if (pending_rate->unsaved && (closing || now - pending_rate->changed_at >= kFrameRateSettle)) {
      pending_rate->unsaved = false;
      Commit(kFrameRate, Live(kFrameRate.name).c_str(), config, saved);
    }
  });
  return screen;
}

}  // namespace fm4_static_recomp::ui
