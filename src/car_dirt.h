#pragma once

#include <string>
#include <string_view>

namespace fm4_static_recomp {

// fm4_car_dirt: the dirt layer cars gather in races. Its texture coordinate is an angle around
// the car that wraps on the front centre line; without the SDK's wrap seam fix thin lines run
// down the nose there, here and in Xenia, never on the console.

// The shaders of the dirt layer, comma-separated: the vertex shaders that compute the angle
// into interpolator 0 z, and the pixel shaders that sample the dirt at it and scale it by c57.y.
std::string DirtVertexShaderList();
std::string DirtPixelShaderList();

// The value FM4 writes to gpu_pixel_shader_constant_override for MOTION BLUR off (the road
// detail's constant), CAR DIRT off (c57.y of the dirt pixel shaders) and the replay cameras'
// motion blur off (its camera reprojection).
std::string ComposePixelShaderConstantOverride(bool road_detail_blur, bool car_dirt,
                                               bool replay_blur);

// Whether FM4's switches may write gpu_pixel_shader_constant_override: not when it was given
// at launch, a diagnostic that wins.
bool SwitchesOwnOverride(std::string_view override_at_launch);

}  // namespace fm4_static_recomp
