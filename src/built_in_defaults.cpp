#include "built_in_defaults.h"

#include <array>
#include <string_view>
#include <utility>

#include <rex/cvar.h>
#include <rex/logging.h>

REXCVAR_DEFINE_STRING(fm4_config_root, "", "Forza Motorsport 4",
                      "Folder of graphics.toml; empty means the state root's config folder");

namespace fm4_static_recomp {
namespace {

using Default = std::pair<std::string_view, std::string_view>;

constexpr std::array kBeforeGpuPlugin = {Default{"gpu_backend", "vulkan"}};

// clear_memory_page_state: the SDK default re-uploads every CPU-written page each frame.
// texture_cache_memory_limit_soft: a race needs about 300 MB at 2x; 256 saved 300 MB.
// async_shader_compilation: a draw whose pipeline is still compiling is dropped, and FM4
// keeps some render targets across frames, so a cold cache darkened the asphalt for seconds.
constexpr std::array kAfterGpuPlugin = {
    Default{"async_shader_compilation", "false"},
    Default{"clear_memory_page_state", "false"},
    Default{"host_present_fps_limit", "0"},
    Default{"gpu_record_thread", "true"},
    Default{"texture_cache_memory_limit_soft", "256"},
    Default{"fh1_async_readbacks", "true"},
    // The SDK's changes since pinyon-shift-v0.3.0, held at the behaviour FM4 was verified
    // with until each is measured here. The SDK's own default otherwise.
    Default{"fh1_msaa_single_sample", "false"},
    Default{"fh1_fold_clears", "false"},
    Default{"fh1_resolve_to_textures", "false"},
    Default{"fh1_direct_resolve_outdated", "false"},
    Default{"fh1_stable_alpha_to_coverage", "false"},
    Default{"fh1_skip_draws_without_effect", "false"},
    Default{"fh1_native_stencil_export", "false"},
    Default{"fh1_specialize_edram_passes", "false"},
    Default{"fh1_async_specialized_passes", "false"},
    Default{"texture_band_reloads", "false"},
    Default{"texture_layer_reloads", "false"},
    Default{"texture_selective_binding_reset", "false"},
    Default{"texture_cache_memory_budget_limits", "false"},
    Default{"vulkan_texture_load_compute_copy", "false"},
    Default{"vulkan_small_texture_copy_views", "false"},
    // With vsync on the SDK presents mailbox or FIFO, which Nvidia composes through the
    // desktop: no independent flip, so no variable refresh rate, and mailbox dropped from
    // 80 to 50 fps after an alt-tab.
    Default{"vulkan_present_mode", "immediate"},
    Default{"swap_post_effect_scene_only", "false"},
    Default{"execute_unclipped_draw_vs_on_cpu", "false"},
    Default{"force_convert_quad_lists_to_triangle_lists", "false"},
    Default{"wait_reg_mem_yield_us", "2000"},
    Default{"gpu_draw_unchanged_inputs", "false"},
    Default{"gpu_system_constants_memo", "false"},
    Default{"gpu_sampler_fetch_key", "false"},
    Default{"gpu_record_coalesce_scratch", "false"},
    Default{"gpu_record_fused_draw_registers", "false"},
    Default{"gpu_record_merge_constant_runs", "false"},
    Default{"gpu_record_shader_load_entries", "false"},
    Default{"gpu_decode_compare_runs_wide", "false"},
    Default{"gpu_d3d_shader_cache", "0"},
    Default{"guest_reservation_sections_unlocked", "false"},
    Default{"rtl_critical_section_pcr_thread", "false"},
    Default{"xma_direct_decode", "false"},
    Default{"xma_lazy_decoder_state", "false"},
    Default{"pad_chord_debug_overlay", ""},
    Default{"pad_chord_game_menu", ""},
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

}  // namespace fm4_static_recomp
