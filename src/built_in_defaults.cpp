#include "built_in_defaults.h"

#include <array>
#include <string_view>
#include <utility>

#include <rex/cvar.h>
#include <rex/logging.h>

REXCVAR_DEFINE_STRING(fm4_config_root, "", "Forza Motorsport 4",
                      "Folder of graphics.toml; empty means the state root's config folder");

namespace fm4_native {
namespace {

using Default = std::pair<std::string_view, std::string_view>;

constexpr std::array kBeforeGpuPlugin = {Default{"gpu_backend", "vulkan"}};

// clear_memory_page_state: the SDK default re-uploads every CPU-written page each frame.
// texture_cache_memory_limit_soft: a race needs about 300 MB at 2x; 256 saved 300 MB.
constexpr std::array kAfterGpuPlugin = {
    Default{"clear_memory_page_state", "false"},
    Default{"host_present_fps_limit", "0"},
    Default{"gpu_record_thread", "true"},
    Default{"texture_cache_memory_limit_soft", "256"},
    Default{"fh1_async_readbacks", "true"},
};

template <size_t N>
void Apply(const std::array<Default, N>& defaults) {
  for (const auto& [name, value] : defaults) {
    if (rex::cvar::GetFlagSource(name) != rex::cvar::Source::kDefault) {
      continue;
    }
    if (rex::cvar::SetFlagByName(name, value)) {
      REXLOG_INFO("Built-in default: {} = {}", name, value);
    } else {
      REXLOG_ERROR("Built-in default: {} refused {}", name, value);
    }
  }
}

}  // namespace

void ApplyBuiltInDefaults(DefaultStage stage) {
  if (stage == DefaultStage::kBeforeGpuPlugin) {
    Apply(kBeforeGpuPlugin);
  } else {
    Apply(kAfterGpuPlugin);
  }
}

}  // namespace fm4_native
