#pragma once

#include <functional>

namespace fm4_static_recomp {

// Runs on a guest thread when the player picks GRAPHICS in My Profile ->
// Options.
void SetGraphicsPageOpener(std::function<void()> opener);

// Makes fm4_road_detail_blur drive the GPU settings, now and on each change;
// call once the GPU plugin has registered them.
void InstallRoadDetailBlurSwitch();

}  // namespace fm4_static_recomp
