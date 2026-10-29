#include "built_in_defaults.h"

#include <array>
#include <cstdint>
#include <string_view>
#include <utility>

#include <rex/cvar.h>
#include <rex/logging.h>

REXCVAR_DEFINE_STRING(fm4_config_root, "", "Forza Motorsport 4",
                      "Folder of graphics.toml and controls.toml; empty means the state root's "
                      "config folder");

namespace fm4_static_recomp {
namespace {

using Default = std::pair<std::string_view, std::string_view>;

constexpr uint32_t kIntelVendorId = 0x8086;

constexpr std::array kBeforeGpuPlugin = {Default{"gpu_backend", "vulkan"}};

#if defined(__linux__) && !defined(__ANDROID__)
// linux_thread_cores auto is the layout Pinyon Shift measured for Forza Horizon on the Steam
// Deck: it names FH1's simulation, audio and render threads by their entry points and puts
// every other thread on two SMT siblings (cpu 5 and 7). None of FM4's threads match, so all of
// them, the game's own simulation and render threads included, would share those two. The
// scheduler places them instead, as on any other Linux PC.
constexpr std::array kLinuxBeforeGpuPlugin = {Default{"linux_thread_cores", "off"}};
#endif

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
    Default{"fh1_async_specialized_passes", "false"},
    Default{"texture_band_reloads", "false"},
    Default{"texture_layer_reloads", "false"},
    Default{"texture_selective_binding_reset", "false"},
    Default{"texture_cache_memory_budget_limits", "false"},
    Default{"vulkan_texture_load_compute_copy", "false"},
    Default{"vulkan_small_texture_copy_views", "false"},
    Default{"fh1_readback_small_float_resolves", "false"},
    // With vsync on the SDK presents mailbox or FIFO, which Nvidia composes through the
    // desktop: no independent flip, so no variable refresh rate, and mailbox dropped from
    // 80 to 50 fps after an alt-tab.
    Default{"vulkan_present_mode", "immediate"},
    Default{"swap_post_effect_scene_only", "false"},
    Default{"execute_unclipped_draw_vs_on_cpu", "false"},
    Default{"force_convert_quad_lists_to_triangle_lists", "false"},
    Default{"wait_reg_mem_yield_us", "2000"},
    Default{"gpu_d3d_shader_cache", "0"},
    Default{"guest_reservation_sections_unlocked", "false"},
    Default{"rtl_critical_section_pcr_thread", "false"},
    Default{"xma_direct_decode", "false"},
    Default{"xma_lazy_decoder_state", "false"},
    Default{"pad_chord_debug_overlay", ""},
    // The keyboard plays beside the pad, Left Shift as the clutch while driving keys are
    // held; no mouse input (the game's menus have no pointer).
    Default{"mnk_mode", "true"},
    Default{"mnk_exact_modifiers", "false"},
    Default{"mnk_mouse", "false"},
    Default{"mnk_mouse_steering", "false"},
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
#if defined(__linux__) && !defined(__ANDROID__)
    Apply(kLinuxBeforeGpuPlugin);
#endif
  } else {
    Apply(kAfterGpuPlugin);
  }
}

// Intel GPUs flicker the scene's lights with the generic resolve and EDRAM transfer passes.
void ApplyGpuVendorDefaults(uint32_t vendor_id) {
  Apply(std::array{
      Default{"fh1_specialize_edram_passes", vendor_id == kIntelVendorId ? "true" : "false"}});
}

}  // namespace fm4_static_recomp
