#pragma once

#include <cstdint>

namespace fm4_static_recomp {

enum class DefaultStage { kBeforeGpuPlugin, kAfterGpuPlugin };

// The values run.ps1 used to pass, set only while a setting still has its
// compiled-in value, so a launch option or fm4_static_recomp.toml wins. GPU settings
// exist only once the plugin is loaded, hence the two stages.
void ApplyBuiltInDefaults(DefaultStage stage);

// The same for settings that depend on the GPU, once the device is chosen and before the
// game starts.
void ApplyGpuVendorDefaults(uint32_t vendor_id);

}  // namespace fm4_static_recomp
