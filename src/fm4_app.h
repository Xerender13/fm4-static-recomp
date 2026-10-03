#pragma once

#include <memory>

#include <rex/rex_app.h>
#include <rex/ui/windowed_app_context.h>

#include "disc2_install.h"

class Fm4App final : public rex::ReXApp {
 public:
  using rex::ReXApp::ReXApp;

  static std::unique_ptr<rex::ui::WindowedApp> Create(rex::ui::WindowedAppContext& context) {
    return std::unique_ptr<Fm4App>(new Fm4App(context, "fm4_native", PPCImageConfig));
  }

 protected:
  void OnPreSetup(rex::RuntimeConfig& config) override {
    config.gpu_plugin = "fh1";
  }

  void OnPostSetup() override {
    fm4_native::InstallContentPackages(*runtime());
  }
};
