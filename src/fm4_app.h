#pragma once

#include <memory>

#include <rex/cvar.h>
#include <rex/rex_app.h>
#include <rex/system/gpu_plugin.h>
#include <rex/ui/overlay/achievement_notification.h>
#include <rex/ui/windowed_app_context.h>

#include "disc2_install.h"
#include "game_hooks.h"
#include "scripted_input.h"

class Fm4App final : public rex::ReXApp {
 public:
  using rex::ReXApp::ReXApp;

  static std::unique_ptr<rex::ui::WindowedApp> Create(rex::ui::WindowedAppContext& context) {
    return std::unique_ptr<Fm4App>(new Fm4App(context, "fm4_native", PPCImageConfig));
  }

 protected:
  void OnPreSetup(rex::RuntimeConfig& config) override {
    config.gpu_plugin = "fh1";
    fm4_native::ConfigureScriptedInput(config);
    // The GPU settings are registered by the plugin, so it is loaded here
    // rather than by the app, and the saved values reach them before the GPU
    // is set up.
    config.graphics =
        rex::system::LoadGpuPlugin(config.gpu_plugin, rex::cvar::GetFlagByName("gpu_backend"));
    fm4_native::InstallRoadDetailBlurSwitch();
  }

  void OnPostSetup() override {
    fm4_native::InstallContentPackages(*runtime());
    fm4_native::StartCaptureWatcher([this]() -> rex::ui::Presenter* {
      return runtime() && runtime()->graphics_system() ? runtime()->graphics_system()->presenter()
                                                       : nullptr;
    });
  }

  // A registered dialog, even one with nothing to draw, moves presentation to
  // the UI thread, which repaints at the display rate on top of every guest
  // frame: ~290 presents of 120 frames, and a present held back (an external
  // frame limiter) no longer throttles the guest.
  std::unique_ptr<rex::ui::AchievementNotificationDialog> CreateAchievementNotificationDialog()
      override {
    return nullptr;
  }
};
