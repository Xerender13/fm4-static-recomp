#pragma once

#include <cstdint>

namespace fm4_static_recomp {

// fm4_shadow_map_size: the car shadow map's size in guest pixels, 512 (the console's, 4x MSAA)
// to 768 (2x MSAA). 0 picks it: 768 where a temporal upscaler takes the 1x frame to the
// display, whose detail the console's map then lacks; 512 otherwise.
uint32_t ShadowMapSize(int32_t setting, uint32_t draw_resolution_scale, bool temporal_upscaler);

// fm4_ground_shadow_size: the races' ground shadow ("grey") map, the car seen from the sun that
// every track draw samples, 256 (the console's) to 768. 0 picks it: 512 (the showroom's) where
// a temporal upscaler takes the 1x frame, whose 256 texels then show as blocks; 256 otherwise.
uint32_t GroundShadowSize(int32_t setting, uint32_t draw_resolution_scale, bool temporal_upscaler);

}  // namespace fm4_static_recomp
