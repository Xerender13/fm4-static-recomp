#include "car_dirt.h"

#include <array>

namespace fm4_static_recomp {

namespace {

// Found by a scan of the shaders of the shipped pipeline list: the vertex shaders write
// saturate(atan2(lat, lon) / 2pi + 0.5) + c116.x to o0.z; the pixel shaders fetch at an
// unmodified r0.z and scale the dirt by c57.y, 0 at a race start.
constexpr std::array<std::string_view, 8> kDirtVertexShaders = {
    "07E682FAD0471885", "0D93D5C7F94E90A5", "42CB1356564CF9FA", "5600489C79F0D511",
    "5C8D739C61E85EC7", "8D05A46C3584CA9E", "C897F0DAB1B7FAD7", "E3C51FA0A0B2C1EF",
};
constexpr std::array<std::string_view, 31> kDirtPixelShaders = {
    "06BE1936C4522FFC", "08E3A599907F4E8C", "0DD6E71970442D74", "0F4A07657AF682FB",
    "134B2725B75271DC", "1D1DBE92CED7E7D7", "1F8A0299BFFB5109", "30B95D950B29B7FF",
    "37AF896A2CBCE528", "3FF6978DD63DDB54", "403213A5BCEDF8CA", "4042C4CC34BCA73D",
    "46CB671D893E6709", "5AFF2866B278F96D", "5FE934B3878F2660", "670B85AE4903DC7E",
    "71C34CF563C167CA", "74A074E5889D0959", "7FDDAE0F0B88E28A", "892947FF214FF4E8",
    "8E39781009A79A81", "B1C51D0C3686C920", "B85EF04779ABD438", "C5862561EE9EC6CB",
    "C58B812D2C1BF687", "C6A382D1784CEFCA", "D9AA68D7F015BD51", "E5B1A71249D0CA9A",
    "F3F74C657AC30AAD", "F53EE850DFF13F04", "F57EF6715B0638C3",
};

// The road detail pass (PS 79B4914F) writes the grain's luminance times c5.w plus the speed
// streaks times c5.x into the texture the road samples. A still camera gives c5 = (0, 0, 0, 1).
// The track's DetailBlur switch is no way to turn the blur off: without it the road samples the
// raw grain texture, about three times the contrast.
constexpr std::string_view kRoadDetailStill = "79B4914F27ECEC73:c5=0,0,0,1";

// The replay cameras' motion blur (PS 4746CAD3, after the scene; races never draw it) takes
// twelve taps of the scene along each pixel's motion, from its depth through the camera
// reprojection in c0-c3. The identity leaves every pixel where it is.
constexpr std::string_view kReplayBlurStill =
    "4746CAD32EF168CF:c0=1,0,0,0;4746CAD32EF168CF:c1=0,1,0,0;"
    "4746CAD32EF168CF:c2=0,0,1,0;4746CAD32EF168CF:c3=0,0,0,1";

template <size_t N>
std::string Join(const std::array<std::string_view, N>& hashes) {
  std::string list;
  for (const std::string_view hash : hashes) {
    if (!list.empty()) {
      list += ',';
    }
    list += hash;
  }
  return list;
}

}  // namespace

std::string DirtVertexShaderList() { return Join(kDirtVertexShaders); }

std::string DirtPixelShaderList() { return Join(kDirtPixelShaders); }

std::string ComposePixelShaderConstantOverride(bool road_detail_blur, bool car_dirt,
                                               bool replay_blur) {
  std::string value;
  if (!road_detail_blur) {
    value = kRoadDetailStill;
  }
  if (!car_dirt) {
    for (const std::string_view hash : kDirtPixelShaders) {
      if (!value.empty()) {
        value += ';';
      }
      value += hash;
      value += ":c57.y=0";
    }
  }
  if (!replay_blur) {
    if (!value.empty()) {
      value += ';';
    }
    value += kReplayBlurStill;
  }
  return value;
}

bool SwitchesOwnOverride(std::string_view override_at_launch) { return override_at_launch.empty(); }

}  // namespace fm4_static_recomp
