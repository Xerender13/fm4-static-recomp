#pragma once

#include <cstdint>

namespace fm4_static_recomp {

// fm4_shadow_map_size: the car shadow map's size in guest pixels, 512 (the console's, 4x MSAA)
// to 768 (2x MSAA). 0 picks it: 768 where a temporal upscaler takes the 1x frame to the
// display, whose detail the console's map then lacks; 512 otherwise.
uint32_t ShadowMapSize(int32_t setting, uint32_t draw_resolution_scale, bool temporal_upscaler);

}  // namespace fm4_static_recomp
