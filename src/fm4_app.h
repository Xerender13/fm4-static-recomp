#pragma once

#include <algorithm>
#include <filesystem>
#include <memory>
#include <span>
#include <string>
#include <vector>

#include <rex/cvar.h>
#include <rex/input/input_system.h>
#include <rex/logging.h>
#include <rex/rex_app.h>
#include <rex/system/gpu_plugin.h>
#include <rex/system/xmemory.h>
#include <rex/ui/flags.h>
#include <rex/ui/keybinds.h>
#include <rex/ui/overlay/achievement_notification.h>
#include <rex/ui/window.h>
#include <rex/ui/windowed_app_context.h>

#include "built_in_defaults.h"
#include "config/host_config.h"
#include "disc2_install.h"
#include "game_hooks.h"
#include "scripted_input.h"
#include "shader_cache/cache_writer.h"
#include "shader_cache/disc_shaders.h"
#include "tool_mode.h"
#include "ui/graphics_page.h"
#include "ui/hostui/host_ui.h"

REXCVAR_DECLARE(std::string, fm4_config_root);
REXCVAR_DECLARE(std::string, fm4_write_graphics_schema);
REXCVAR_DECLARE(std::string, fm4_dump_disc_shaders);
REXCVAR_DECLARE(std::string, fm4_build_shader_cache);
REXCVAR_DECLARE(std::string, fm4_shader_list_dir);

class Fm4App final : public rex::ReXApp {
 public:
  using rex::ReXApp::ReXApp;

  static std::unique_ptr<rex::ui::WindowedApp> Create(rex::ui::WindowedAppContext& context) {
    return std::unique_ptr<Fm4App>(new Fm4App(context, "fm4_static_recomp", PPCImageConfig));
  }

 protected:
  void OnConfigurePaths(rex::PathConfig& paths) override {
    const std::string configured = REXCVAR_GET(fm4_config_root);
    const std::filesystem::path config_root =
        configured.empty() ? paths.user_data_root.parent_path() / "config"
                           : std::filesystem::path(configured);
    graphics_config_ =
        std::make_unique<fm4_static_recomp::config::HostConfig>(config_root / "graphics.toml");
  }

  void OnPreSetup(rex::RuntimeConfig& config) override {
    const fm4_static_recomp::ToolMode mode = fm4_static_recomp::CurrentToolMode();
    if (mode == fm4_static_recomp::ToolMode::kInstall ||
        mode == fm4_static_recomp::ToolMode::kShaderCache) {
      config.gpu_plugin.clear();
      config.audio_factory = nullptr;
      config.tool_mode = true;
      return;
    }
    config.gpu_plugin = "fh1";
    fm4_static_recomp::ConfigureScriptedInput(config);
    fm4_static_recomp::ApplyBuiltInDefaults(fm4_static_recomp::DefaultStage::kBeforeGpuPlugin);
    // The GPU settings are registered by the plugin, so it is loaded here
    // rather than by the app, and the saved values reach them before the GPU
    // is set up.
    config.graphics =
        rex::system::LoadGpuPlugin(config.gpu_plugin, rex::cvar::GetFlagByName("gpu_backend"));
    if (mode == fm4_static_recomp::ToolMode::kSchema) {
      return;
    }
    fm4_static_recomp::ApplyBuiltInDefaults(fm4_static_recomp::DefaultStage::kAfterGpuPlugin);
    if (graphics_config_->Load()) {
      fm4_static_recomp::ui::ApplySavedGraphicsSettings(*graphics_config_);
    } else {
      REXLOG_ERROR("Graphics settings: cannot read {}", graphics_config_->path().string());
    }
    fm4_static_recomp::InstallRoadDetailBlurSwitch();
  }

  bool WantsPresentation() const override {
    return fm4_static_recomp::CurrentToolMode() == fm4_static_recomp::ToolMode::kNone;
  }

  bool ConstructRuntime(const rex::PathConfig& paths) override {
    if (fm4_static_recomp::CurrentToolMode() == fm4_static_recomp::ToolMode::kSchema) {
      const std::filesystem::path path = std::string(REXCVAR_GET(fm4_write_graphics_schema));
      if (fm4_static_recomp::ui::WriteGraphicsSchema(path)) {
        REXLOG_INFO("Graphics schema written to {}", path.string());
      } else {
        REXLOG_ERROR("Graphics schema: cannot write {}", path.string());
      }
      return true;
    }
    if (!rex::ReXApp::ConstructRuntime(paths)) {
      return false;
    }
    if (fm4_static_recomp::CurrentToolMode() == fm4_static_recomp::ToolMode::kShaderCache) {
      return RunShaderCacheTool();
    }
    return true;
  }

  void LaunchModule() override {
    if (fm4_static_recomp::CurrentToolMode() == fm4_static_recomp::ToolMode::kNone) {
      rex::ReXApp::LaunchModule();
      return;
    }
    app_context().RequestDeferredQuit();
  }

  void OnPostSetup() override {
    fm4_static_recomp::InstallContentPackages(*runtime());
    if (fm4_static_recomp::CurrentToolMode() != fm4_static_recomp::ToolMode::kNone) {
      return;
    }
    if (rex::ui::Window* const window = this->window()) {
      window->SetCursorVisibility(rex::ui::Window::CursorVisibility::kAutoHidden);
      fm4_static_recomp::ui::LimitFrameRateToDisplay(
          fm4_static_recomp::ui::DisplayRefreshHz(window->GetNativeWindowHandle()));
    }
    fm4_static_recomp::StartCaptureWatcher([this]() -> rex::ui::Presenter* {
      return runtime() && runtime()->graphics_system() ? runtime()->graphics_system()->presenter()
                                                       : nullptr;
    });
    fm4_static_recomp::SetGraphicsPageOpener([this] {
      if (rex::ui::Window* const window = this->window()) {
        window->app_context().CallInUIThreadDeferred([this] { OpenGraphicsPage(); });
      }
    });
    rex::ui::RegisterBind("bind_graphics_page", "F6", "Toggle the GRAPHICS page", [this] {
      if (host_ui_ && host_ui_->is_open()) {
        host_ui_->Close();
      } else {
        OpenGraphicsPage();
      }
    });
  }

  void OnShutdown() override {
    rex::ui::UnregisterBind("bind_graphics_page");
    fm4_static_recomp::SetGraphicsPageOpener(nullptr);
    host_ui_.reset();
  }

  // A registered dialog, even one with nothing to draw, moves presentation to
  // the UI thread, which repaints at the display rate on top of every guest
  // frame: ~290 presents of 120 frames, and a present held back (an external
  // frame limiter) no longer throttles the guest.
  std::unique_ptr<rex::ui::AchievementNotificationDialog> CreateAchievementNotificationDialog()
      override {
    return nullptr;
  }

 private:
  // Returning false makes the process exit with a failure code.
  bool RunShaderCacheTool() {
    namespace shader_cache = fm4_static_recomp::shader_cache;
    const std::span<const uint8_t> image(
        runtime()->memory()->TranslateVirtual<const uint8_t*>(PPCImageConfig.image_base),
        PPCImageConfig.image_size);
    size_t files = 0;
    std::string error;
    const auto scanned = shader_cache::ScanDisc(game_data_root(), image, &error, &files);
    if (!scanned) {
      REXLOG_ERROR("Shader cache: {}", error);
      return false;
    }
    const std::vector<shader_cache::DiscShader>& shaders = *scanned;
    const size_t pixel = size_t(std::count_if(shaders.begin(), shaders.end(),
                                              [](const auto& shader) { return shader.pixel; }));
    REXLOG_INFO("Disc shaders: {} distinct ({} vertex, {} pixel) from {} files", shaders.size(),
                shaders.size() - pixel, pixel, files);
    const std::string dump = REXCVAR_GET(fm4_dump_disc_shaders);
    if (!dump.empty() && !shader_cache::WriteDump(shaders, dump)) {
      REXLOG_ERROR("Disc shaders: cannot write the dump to {}", dump);
      return false;
    }
    const std::string shareable = REXCVAR_GET(fm4_build_shader_cache);
    if (shareable.empty()) {
      return true;
    }
    const auto result = shader_cache::WriteShaderCache(
        shaders, std::string(REXCVAR_GET(fm4_shader_list_dir)), shareable, &error);
    if (!result) {
      REXLOG_ERROR("Shader cache: {}", error);
      return false;
    }
    if (!result->shaders_written && !result->pipelines_added && !result->pipelines_skipped) {
      REXLOG_INFO("Shader cache: already complete, nothing to add ({} of {} shaders, {} not on the "
                  "disc; {} pipelines)",
                  result->shaders_needed - result->shaders_missing, result->shaders_needed,
                  result->shaders_missing, result->pipelines_listed);
      return true;
    }
    const std::string pipelines =
        result->cache_version
            ? fmt::format("pipelines skipped: list version {:08X}, cache version {:08X}",
                          result->list_version, result->cache_version)
        : result->pipelines_skipped
            ? fmt::format("pipelines skipped: list version {:08X}, tool {:08X}",
                          result->list_version, shader_cache::kPipelineStorageVersion)
            : fmt::format("{} of {} pipelines added", result->pipelines_added,
                          result->pipelines_listed);
    REXLOG_INFO("Shader cache: {} of {} shaders ({} not on the disc), {}", result->shaders_written,
                result->shaders_needed, result->shaders_missing, pipelines);
    return true;
  }

  void OpenGraphicsPage() {
    if (host_ui_ && host_ui_->is_open()) {
      return;
    }
    rex::ui::Presenter* const presenter =
        runtime() && runtime()->graphics_system() ? runtime()->graphics_system()->presenter()
                                                  : nullptr;
    if (!presenter || !immediate_drawer() || !window() || !graphics_config_) {
      REXLOG_WARN("Graphics page: presentation is not ready");
      return;
    }
    if (!host_ui_) {
      host_ui_ = std::make_unique<fm4_static_recomp::hostui::HostUi>(
          *this, *presenter, *immediate_drawer(), *window(),
          static_cast<rex::input::InputSystem*>(runtime()->input_system()), game_data_root());
    }
    fm4_static_recomp::ui::GraphicsPageServices services;
    services.display_refresh_hz = [hz = fm4_static_recomp::ui::DisplayRefreshHz(
                                       window()->GetNativeWindowHandle())] { return hz; };
    services.draw_resolution_scale = [this]() -> uint32_t {
      auto* graphics = runtime() ? runtime()->graphics_system() : nullptr;
      return graphics ? graphics->draw_resolution_scale() : 0;
    };
    services.output_size = [this]() -> std::optional<std::pair<uint32_t, uint32_t>> {
      const uint32_t width = window() ? window()->GetActualPhysicalWidth() : 0;
      const uint32_t height = window() ? window()->GetActualPhysicalHeight() : 0;
      if (!width || !height) {
        return std::nullopt;
      }
      if (!REXCVAR_GET(present_letterbox)) {
        return std::pair{width, height};
      }
      return uint64_t(width) * 9 > uint64_t(height) * 16 ? std::pair{height * 16 / 9, height}
                                                         : std::pair{width, width * 9 / 16};
    };
    services.saved = [this] { host_ui_->NotifySaved(); };
    host_ui_->Open(
        fm4_static_recomp::ui::CreateGraphicsPage(*graphics_config_, std::move(services)));
  }

  std::unique_ptr<fm4_static_recomp::config::HostConfig> graphics_config_;
  std::unique_ptr<fm4_static_recomp::hostui::HostUi> host_ui_;
};
