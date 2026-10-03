#pragma once

#include <memory>

#include <rex/rex_app.h>
#include <rex/ui/windowed_app_context.h>

#include "disc2_install.h"
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
  }

  void OnPostSetup() override {
    fm4_native::InstallContentPackages(*runtime());
    fm4_native::StartCaptureWatcher([this]() -> rex::ui::Presenter* {
      return runtime() && runtime()->graphics_system() ? runtime()->graphics_system()->presenter()
                                                       : nullptr;
    });
  }
};
