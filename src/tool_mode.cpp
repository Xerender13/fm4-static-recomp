#include "tool_mode.h"

#include <string>

#include <rex/cvar.h>

REXCVAR_DEFINE_BOOL(fm4_install_only, false, "Forza Motorsport 4",
                    "Install --fm4_disc2_packages and --fm4_dlc_packages, then quit");
REXCVAR_DEFINE_STRING(fm4_write_graphics_schema, "", "Forza Motorsport 4",
                      "Write the GRAPHICS rows as JSON to this file, then quit");

namespace fm4_native {

ToolMode CurrentToolMode() {
  if (!std::string(REXCVAR_GET(fm4_write_graphics_schema)).empty()) {
    return ToolMode::kSchema;
  }
  return REXCVAR_GET(fm4_install_only) ? ToolMode::kInstall : ToolMode::kNone;
}

}  // namespace fm4_native
