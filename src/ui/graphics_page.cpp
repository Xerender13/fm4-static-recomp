#include "ui/graphics_page.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdlib>
#include <fstream>
#include <iterator>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include <rex/cvar.h>
#include <rex/logging.h>

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
const Setting kDetail = {"fm4_level_of_detail",
                         true,
                         {{"CONSOLE", "console"},
                          {"HIGH", "high"},
                          {"ULTRA", "ultra"}}};
const Setting kScale = {"resolution_scale", false, {{"1X", "1"}, {"2X", "2"}, {"3X", "3"}, {"4X", "4"}}};
const Setting kPostAa = {
    "swap_post_effect", true, {{"OFF", "none"}, {"FXAA", "fxaa"}, {"FXAA EXTREME", "fxaa_extreme"}}};
// OFF keeps the game's MSAA surfaces single-sampled above 1x.
const Setting kGameMsaa = {"fh1_scaled_msaa_single_sample", false, {{"ON", "false"}, {"OFF", "true"}}};
const Setting kAnisotropy = {"anisotropic_override",
                             false,
                             {{"GAME", "-1"}, {"2X", "2"}, {"4X", "3"}, {"8X", "4"}, {"16X", "5"}}};

const Setting kMotionBlur = {"fm4_road_detail_blur", false, {{"ON", "true"}, {"OFF", "false"}}};
const Setting kDepthOfField = {"fm4_depth_of_field", false, {{"ON", "true"}, {"OFF", "false"}}};

const Setting kRaceMsaa = {"fm4_race_4x_msaa", false, {{"2X", "false"}, {"4X", "true"}}};
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

// Every setting but the frame rate and the resolution scale.
const Setting* const kPresetSettings[] = {
    &kPostAa, &kGameMsaa, &kRaceMsaa, &kAnisotropy, &kSceneryShadows, &kReflectionUpdates,
    &kDetail, &kMirror, &kBloom, &kSoftParticles, &kCrowds, &kMotionBlur, &kDepthOfField};
constexpr size_t kPresetSettingCount = std::size(kPresetSettings);

const Setting* const kSettings[] = {
    &kFrameRate, &kScale, &kPostAa, &kGameMsaa, &kRaceMsaa, &kAnisotropy, &kSceneryShadows,
    &kReflectionUpdates, &kDetail, &kMirror, &kBloom, &kSoftParticles, &kCrowds, &kMotionBlur,
    &kDepthOfField};

struct Preset {
  const char* label;
  std::array<const char*, kPresetSettingCount> values;  // in kPresetSettings' order
};

// The page shows the preset all settings match, or CUSTOM.
const Preset kPresets[] = {
    {"CONSOLE",
     {"none", "false", "false", "-1", "false", "console", "console", "false", "false", "false",
      "false", "true", "true"}},
    {"HIGH",
     {"fxaa", "true", "false", "5", "true", "console", "high", "false", "true", "false",
      "false", "true", "true"}},
    {"ULTRA",
     {"fxaa_extreme", "true", "false", "5", "true", "every_race", "ultra", "true", "true",
      "true", "false", "true", "true"}},
    {"EXPERIMENTAL",
     {"fxaa_extreme", "true", "true", "5", "true", "double", "ultra", "true", "true", "true",
      "true", "true", "true"}},
};
constexpr int kPresetCount = int(std::size(kPresets));

struct RowText {
  const char* label;
  const Setting* setting;  // null for the preset row
  const char* description;
  bool divider_above = false;
};

// The page's rows in order; the frame rate, scale and preset rows add live
// behaviour of their own in CreateGraphicsPage.
const RowText kRows[] = {
    {"FRAME RATE", &kFrameRate,
     "Frames per second in races, from the console's 60 up to your display's refresh rate. A "
     "rate the GPU cannot hold stutters: lower it or the resolution scale."},
    {"RESOLUTION SCALE", &kScale,
     "Renders the game at a multiple of 1280x720: sharper edges and textures, more GPU load."},
    {"GRAPHICS PRESET", nullptr,
     "Sets every setting below. Console is the game as on the console; High raises most and "
     "adds FXAA; Ultra also raises the level of detail, the mirror and particles and uses FXAA "
     "Extreme; Experimental adds 4x MSAA in races, double reflection updates and crowds in "
     "every race. A change to one setting makes the preset Custom."},
    {"GAME MSAA", &kGameMsaa,
     "The game's own multisampling of its scenes. Off is faster above 1x and leaves edges to "
     "the resolution scale.",
     true},
    {"RACE MSAA", &kRaceMsaa,
     "The game's multisampling in races: 4x has twice the samples of the console's 2x and "
     "draws the scene in three tiles instead of two. Heavy on the GPU."},
    {"ANISOTROPIC FILTERING", &kAnisotropy,
     "Keeps textures sharp at grazing angles. Game leaves each texture the filtering the game "
     "asks for."},
    {"LEVEL OF DETAIL", &kDetail,
     "How far cars keep their detailed models. Ultra also keeps the best model loaded for your "
     "car, at more guest memory per car. Applies from the next race."},
    {"SCENERY SHADOWS", &kSceneryShadows,
     "Shadows of the cones and barriers in races, which the game draws only in replays and "
     "photo mode."},
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
     "Smooths edges in the finished picture. FXAA Extreme smooths more and softens more.", true},
    {"BLOOM", &kBloom,
     "Full Rate updates the glow every frame, where the console skips every second one "
     "(16-car races)."},
    {"MOTION BLUR", &kMotionBlur,
     "Blurs the road surface with speed. Off keeps the asphalt as sharp as when standing "
     "still."},
    {"DEPTH OF FIELD", &kDepthOfField,
     "Blurs what is out of focus in the showroom, pre-race and replay cameras and photo mode. "
     "Applies from the next screen."},
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
  // ApplySavedGraphicsSettings turns GAME MSAA off unless a launch option says otherwise.
  if (&setting == &kGameMsaa) {
    return "true";
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
  if (output_width == render_width && output_height == render_height) {
    return line + ", shown at that size.";
  }
  if (output_width * output_height > render_width * render_height) {
    return line + ", stretched to " + size(output_width, output_height) + ".";
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
  // GAME MSAA OFF, as in every preset but CONSOLE: above 1x the single-sampled surfaces' alpha
  // to coverage dither beats the game's MSAA on cut-outs (thirty-third session).
  if (!SetByLaunchOption(kGameMsaa)) {
    rex::cvar::SetFlagByName(kGameMsaa.name, "true");
  }
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

  MenuRow msaa = Row("GAME MSAA", kGameMsaa, config, services);
  msaa.note = [services] {
    return LiveScale(services) < 2 ? std::string("Does nothing at 1x.") : std::string();
  };
  rows.push_back(std::move(msaa));

  MenuRow race_msaa = Row("RACE MSAA", kRaceMsaa, config, services);
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

  MenuRow post_aa = Row("POST AA", kPostAa, config, services);
  rows.push_back(std::move(post_aa));

  MenuRow bloom = Row("BLOOM", kBloom, config, services);
  rows.push_back(std::move(bloom));

  MenuRow motion_blur = Row("MOTION BLUR", kMotionBlur, config, services);
  rows.push_back(std::move(motion_blur));

  MenuRow depth_of_field = Row("DEPTH OF FIELD", kDepthOfField, config, services);
  rows.push_back(std::move(depth_of_field));

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
