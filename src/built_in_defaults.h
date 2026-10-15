#pragma once

namespace fm4_native {

enum class DefaultStage { kBeforeGpuPlugin, kAfterGpuPlugin };

// The values run.ps1 used to pass, set only while a setting still has its
// compiled-in value, so a launch option or fm4_native.toml wins. GPU settings
// exist only once the plugin is loaded, hence the two stages.
void ApplyBuiltInDefaults(DefaultStage stage);

}  // namespace fm4_native
