#pragma once

namespace fm4_native {

// Makes fm4_road_detail_blur drive the GPU settings, now and on each change;
// call once the GPU plugin has registered them.
void InstallRoadDetailBlurSwitch();

}  // namespace fm4_native
