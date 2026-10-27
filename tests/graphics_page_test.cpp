#include <catch2/catch_test_macros.hpp>

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#include <rex/cvar.h>
#include <rex/ui/upscaler.h>

#include "ui/graphics_page.h"

namespace {

namespace config = fm4_static_recomp::config;
namespace ui = fm4_static_recomp::ui;
using fm4_static_recomp::hostui::MenuRow;
using fm4_static_recomp::hostui::MenuScreen;

std::filesystem::path TempFile(const char* name) {
  return std::filesystem::temp_directory_path() / ("fm4_graphics_page_test_" + std::string(name));
}

rex::ui::UpscalerInfo Upscaler(const char* id, bool available, const char* reason = "") {
  rex::ui::UpscalerInfo info;
  info.id = id;
  info.kind = rex::ui::UpscalerKind::kTemporal;
  info.available = available;
  info.unavailable_reason = reason;
  return info;
}

// FSR 2 and 3 run, DLSS does not: the page on an AMD GPU.
ui::GraphicsPageServices Services() {
  ui::GraphicsPageServices services;
  services.draw_resolution_scale = [] { return 1u; };
  services.output_size = [] { return std::optional(std::pair{2560u, 1440u}); };
  services.upscalers = [] {
    return std::vector{Upscaler("dlss", false, "needs an NVIDIA RTX GPU"), Upscaler("fsr3", true),
                       Upscaler("fsr2", true)};
  };
  return services;
}

const MenuRow* Find(const MenuScreen& screen, const char* label) {
  const auto& rows = screen.rows();
  const auto it = std::find_if(rows.begin(), rows.end(),
                               [label](const MenuRow& row) { return row.label == label; });
  return it == rows.end() ? nullptr : &*it;
}

// The cvars are the runtime's, shared by every test: each test starts from no upscaler.
struct Upscaling {
  Upscaling(const char* effect, bool active) {
    rex::cvar::SetFlagByName("present_effect", effect);
    rex::ui::SetTemporalUpscalerActive(active);
  }
  ~Upscaling() {
    rex::cvar::SetFlagByName("present_effect", "bilinear");
    rex::ui::SetTemporalUpscalerActive(false);
  }
};

}  // namespace

TEST_CASE("The launcher's schema offers the upscaler, its sharpness and the DLSS preset while DLSS "
          "is chosen",
          "[graphics_page]") {
  const auto path = TempFile("schema.json");
  REQUIRE(ui::WriteGraphicsSchema(path));
  std::stringstream text;
  text << std::ifstream(path).rdbuf();
  const std::string schema = text.str();
  const auto at = [&schema](const char* label) {
    return schema.find(std::string("{\"label\":\"") + label + "\"");
  };
  REQUIRE(at("UPSCALER") != std::string::npos);
  REQUIRE(at("DLSS PRESET") != std::string::npos);
  REQUIRE(at("SHARPNESS") != std::string::npos);
  CHECK(at("RESOLUTION SCALE") < at("UPSCALER"));
  CHECK(at("UPSCALER") < at("DLSS PRESET"));
  CHECK(at("DLSS PRESET") < at("SHARPNESS"));
  CHECK(at("SHARPNESS") < at("GRAPHICS PRESET"));
  CHECK(schema.find("\"key\":\"present_effect\"") != std::string::npos);
  CHECK(schema.find("{\"label\":\"OFF\",\"value\":\"bilinear\"}") != std::string::npos);
  CHECK(schema.find("{\"label\":\"30 %\",\"value\":\"30\"}") != std::string::npos);
  const std::string preset = schema.substr(at("DLSS PRESET"), at("SHARPNESS") - at("DLSS PRESET"));
  CHECK(preset.find("\"key\":\"present_dlss_preset\"") != std::string::npos);
  CHECK(preset.find("\"default\":\"auto\"") != std::string::npos);
  CHECK(preset.find("\"shown_when\":{\"present_effect\":\"dlss\"}") != std::string::npos);
  CHECK(schema.find("shown_when") == schema.rfind("shown_when"));
  std::filesystem::remove(path);
}

TEST_CASE("The in-game page has UPSCALER and SHARPNESS and leaves the DLSS preset to the launcher",
          "[graphics_page]") {
  config::HostConfig config(TempFile("rows.toml"));
  const auto page = ui::CreateGraphicsPage(config, Services());
  CHECK(Find(*page, "UPSCALER"));
  CHECK(Find(*page, "SHARPNESS"));
  CHECK_FALSE(Find(*page, "DLSS PRESET"));
}

TEST_CASE("UPSCALER steps past an upscaler the GPU cannot run", "[graphics_page]") {
  const auto path = TempFile("step.toml");
  config::HostConfig config(path);
  const Upscaling upscaling("bilinear", false);
  const auto page = ui::CreateGraphicsPage(config, Services());
  const MenuRow& row = *Find(*page, "UPSCALER");
  CHECK(row.value() == "OFF");
  row.adjust(+1);
  CHECK(rex::cvar::GetFlagByName("present_effect") == "fsr2");
  row.adjust(+1);
  CHECK(rex::cvar::GetFlagByName("present_effect") == "fsr3");
  row.adjust(+1);
  CHECK(rex::cvar::GetFlagByName("present_effect") == "bilinear");
  row.adjust(-1);
  CHECK(rex::cvar::GetFlagByName("present_effect") == "fsr3");
  CHECK(config.Get("present_effect").value_or("") == "fsr3");
  std::filesystem::remove(path);
}

TEST_CASE("UPSCALER says why a chosen upscaler does not run", "[graphics_page]") {
  config::HostConfig config(TempFile("reason.toml"));
  const Upscaling upscaling("dlss", false);
  const auto page = ui::CreateGraphicsPage(config, Services());
  const MenuRow& row = *Find(*page, "UPSCALER");
  CHECK(row.value() == "DLSS");
  CHECK(row.note() == "DLSS does not run here: needs an NVIDIA RTX GPU.");
}

TEST_CASE("While an upscaler runs, the notes give the render size, output size and factor",
          "[graphics_page]") {
  config::HostConfig config(TempFile("note.toml"));
  const Upscaling upscaling("fsr3", true);
  const auto page = ui::CreateGraphicsPage(config, Services());
  const std::string line = "Renders 1280x720, upscaled to 2560x1440 by FSR 3 (2.0x).";
  CHECK(Find(*page, "UPSCALER")->note() == line);
  CHECK(Find(*page, "RESOLUTION SCALE")->note() == line);
}

TEST_CASE("At the output size the notes name the upscaler's anti-aliasing mode", "[graphics_page]") {
  config::HostConfig config(TempFile("native.toml"));
  auto services = Services();
  services.output_size = [] { return std::optional(std::pair{1280u, 720u}); };
  {
    const Upscaling upscaling("dlss", true);
    const auto page = ui::CreateGraphicsPage(config, services);
    CHECK(Find(*page, "RESOLUTION SCALE")->note() == "Renders 1280x720, shown at that size with DLAA.");
  }
  const Upscaling upscaling("fsr3", true);
  const auto page = ui::CreateGraphicsPage(config, services);
  const std::string line = "Renders 1280x720, shown at that size with FSR 3 Native AA.";
  CHECK(Find(*page, "UPSCALER")->note() == line);
  CHECK(Find(*page, "RESOLUTION SCALE")->note() == line);
}

TEST_CASE("Without an upscaler the notes keep the plain stretch", "[graphics_page]") {
  config::HostConfig config(TempFile("stretch.toml"));
  const Upscaling upscaling("bilinear", false);
  const auto page = ui::CreateGraphicsPage(config, Services());
  CHECK(Find(*page, "RESOLUTION SCALE")->note() == "Renders 1280x720, stretched to 2560x1440.");
  CHECK(Find(*page, "UPSCALER")->note().empty());
}

TEST_CASE("An upscaler takes over POST AA and enables SHARPNESS", "[graphics_page]") {
  config::HostConfig config(TempFile("owned.toml"));
  const auto page = ui::CreateGraphicsPage(config, Services());
  const MenuRow& post_aa = *Find(*page, "POST AA");
  const MenuRow& sharpness = *Find(*page, "SHARPNESS");
  {
    const Upscaling upscaling("bilinear", false);
    CHECK(post_aa.is_enabled());
    CHECK_FALSE(sharpness.is_enabled());
  }
  {
    const Upscaling upscaling("dlss", true);
    CHECK(post_aa.value() == "OFF (DLSS)");
    CHECK_FALSE(post_aa.is_enabled());
    CHECK(sharpness.is_enabled());
    CHECK(sharpness.value() == "30 %");
  }
}
