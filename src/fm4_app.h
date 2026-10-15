#pragma once

#include <filesystem>
#include <memory>
#include <string>

#include <rex/cvar.h>
#include <rex/input/input_system.h>
#include <rex/logging.h>
#include <rex/rex_app.h>
#include <rex/system/gpu_plugin.h>
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
#include "ui/graphics_page.h"
#include "ui/hostui/host_ui.h"

REXCVAR_DECLARE(std::string, fm4_config_root);

class Fm4App final : public rex::ReXApp {
 public:
  using rex::ReXApp::ReXApp;

  static std::unique_ptr<rex::ui::WindowedApp> Create(rex::ui::WindowedAppContext& context) {
    return std::unique_ptr<Fm4App>(new Fm4App(context, "fm4_native", PPCImageConfig));
  }

 protected:
  void OnConfigurePaths(rex::PathConfig& paths) override {
    const std::string configured = REXCVAR_GET(fm4_config_root);
    const std::filesystem::path config_root =
        configured.empty() ? paths.user_data_root.parent_path() / "config"
                           : std::filesystem::path(configured);
    graphics_config_ =
        std::make_unique<fm4_native::config::HostConfig>(config_root / "graphics.toml");
  }

  void OnPreSetup(rex::RuntimeConfig& config) override {
    config.gpu_plugin = "fh1";
    fm4_native::ConfigureScriptedInput(config);
    fm4_native::ApplyBuiltInDefaults(fm4_native::DefaultStage::kBeforeGpuPlugin);
    // The GPU settings are registered by the plugin, so it is loaded here
    // rather than by the app, and the saved values reach them before the GPU
    // is set up.
    config.graphics =
        rex::system::LoadGpuPlugin(config.gpu_plugin, rex::cvar::GetFlagByName("gpu_backend"));
    fm4_native::ApplyBuiltInDefaults(fm4_native::DefaultStage::kAfterGpuPlugin);
    if (graphics_config_->Load()) {
      fm4_native::ui::ApplySavedGraphicsSettings(*graphics_config_);
    } else {
      REXLOG_ERROR("Graphics settings: cannot read {}", graphics_config_->path().string());
    }
    fm4_native::InstallRoadDetailBlurSwitch();
  }

  void OnPostSetup() override {
    if (rex::ui::Window* const window = this->window()) {
      window->SetCursorVisibility(rex::ui::Window::CursorVisibility::kAutoHidden);
      fm4_native::ui::LimitFrameRateToDisplay(
          fm4_native::ui::DisplayRefreshHz(window->GetNativeWindowHandle()));
    }
    fm4_native::InstallContentPackages(*runtime());
    fm4_native::StartCaptureWatcher([this]() -> rex::ui::Presenter* {
      return runtime() && runtime()->graphics_system() ? runtime()->graphics_system()->presenter()
                                                       : nullptr;
    });
    fm4_native::SetGraphicsPageOpener([this] {
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
    fm4_native::SetGraphicsPageOpener(nullptr);
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
      host_ui_ = std::make_unique<fm4_native::hostui::HostUi>(
          *this, *presenter, *immediate_drawer(), *window(),
          static_cast<rex::input::InputSystem*>(runtime()->input_system()), game_data_root());
    }
    fm4_native::ui::GraphicsPageServices services;
    services.display_refresh_hz =
        [hz = fm4_native::ui::DisplayRefreshHz(window()->GetNativeWindowHandle())] { return hz; };
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
    host_ui_->Open(fm4_native::ui::CreateGraphicsPage(*graphics_config_, std::move(services)));
  }

  std::unique_ptr<fm4_native::config::HostConfig> graphics_config_;
  std::unique_ptr<fm4_native::hostui::HostUi> host_ui_;
};
