#include "shadow_map.h"

#include <algorithm>

namespace fm4_static_recomp {

uint32_t ShadowMapSize(int32_t setting, uint32_t draw_resolution_scale, bool temporal_upscaler) {
  if (setting == 0) {
    return draw_resolution_scale == 1 && temporal_upscaler ? 768 : 512;
  }
  return uint32_t(std::clamp(setting, 512, 768));
}

}  // namespace fm4_static_recomp
