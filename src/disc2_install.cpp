#include "disc2_install.h"

#include <filesystem>
#include <string_view>
#include <system_error>

#include <rex/cvar.h>
#include <rex/filesystem.h>
#include <rex/logging.h>
#include <rex/system/kernel_state.h>
#include <rex/system/xam/content_manager.h>
#include <rex/system/xcontent.h>

REXCVAR_DEFINE_STRING(fm4_disc2_packages, "", "Forza Motorsport 4",
                      "Directory holding Disc 2's content packages (4d53091000000001 to "
                      "4d53091000000004), installed into the user data at launch");
REXCVAR_DEFINE_STRING(fm4_dlc_packages, "", "Forza Motorsport 4",
                      "Directory searched recursively for marketplace content packages (DLC), "
                      "each installed into the user data at launch");

namespace fm4_native {

using rex::X_RESULT;

namespace {

void InstallPackage(rex::system::xam::ContentManager& content,
                    const std::filesystem::path& package) {
  // The header is written after the files, so it marks a complete install.
  rex::system::xam::XCONTENT_AGGREGATE_DATA installed;
  if (content.ReadContentHeaderFile(rex::path_to_utf8(package.filename()), 0,
                                    rex::system::xam::kCurrentlyRunningTitleId,
                                    rex::system::XContentType::kMarketplaceContent,
                                    installed) == X_ERROR_SUCCESS) {
    return;
  }
  REXLOG_INFO("Content: installing {}", rex::path_to_utf8(package));
  const X_RESULT result = content.InstallContent(package);
  if (result != X_ERROR_SUCCESS) {
    REXLOG_ERROR("Content: installing {} failed: {:08X}", rex::path_to_utf8(package), uint32_t(result));
  }
}

}  // namespace

void InstallContentPackages(rex::Runtime& runtime) {
  auto& content = *runtime.kernel_state()->content_manager();
  const std::filesystem::path disc2 = REXCVAR_GET(fm4_disc2_packages);
  if (!disc2.empty()) {
    for (std::string_view name : {"4d53091000000001", "4d53091000000002", "4d53091000000003",
                                  "4d53091000000004"}) {
      InstallPackage(content, disc2 / name);
    }
  }
  const std::filesystem::path dlc = REXCVAR_GET(fm4_dlc_packages);
  if (!dlc.empty()) {
    std::error_code ec;
    for (std::filesystem::recursive_directory_iterator it(dlc, ec), end; !ec && it != end;
         it.increment(ec)) {
      if (it->is_regular_file()) InstallPackage(content, it->path());
    }
    if (ec) {
      REXLOG_ERROR("Content: reading {} failed: {}", rex::path_to_utf8(dlc), ec.message());
    }
  }
}

}  // namespace fm4_native
