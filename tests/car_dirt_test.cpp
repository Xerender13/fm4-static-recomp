#include <catch2/catch_test_macros.hpp>

#include <cctype>
#include <string>
#include <string_view>
#include <vector>

#include "car_dirt.h"

using fm4_static_recomp::ComposePixelShaderConstantOverride;
using fm4_static_recomp::DirtPixelShaderList;
using fm4_static_recomp::DirtVertexShaderList;
using fm4_static_recomp::SwitchesOwnOverride;

namespace {

std::vector<std::string> Split(std::string_view text, char separator) {
  std::vector<std::string> parts;
  size_t start = 0;
  while (true) {
    const size_t end = text.find(separator, start);
    parts.emplace_back(text.substr(start, end - start));
    if (end == std::string_view::npos) {
      return parts;
    }
    start = end + 1;
  }
}

bool IsHash(std::string_view text) {
  if (text.size() != 16) {
    return false;
  }
  for (const char c : text) {
    if (!std::isxdigit(static_cast<unsigned char>(c))) {
      return false;
    }
  }
  return true;
}

constexpr std::string_view kRoadEntry = "79B4914F27ECEC73:c5=0,0,0,1";

}  // namespace

TEST_CASE("Motion blur, car dirt and the replay blur on override nothing", "[car_dirt]") {
  CHECK(ComposePixelShaderConstantOverride(true, true, true).empty());
}

TEST_CASE("Motion blur off overrides the road detail only", "[car_dirt]") {
  CHECK(ComposePixelShaderConstantOverride(false, true, true) == kRoadEntry);
}

TEST_CASE("Car dirt off zeroes the dirt amount of every dirt pixel shader", "[car_dirt]") {
  const std::vector<std::string> entries =
      Split(ComposePixelShaderConstantOverride(true, false, true), ';');
  const std::vector<std::string> shaders = Split(DirtPixelShaderList(), ',');
  REQUIRE(entries.size() == shaders.size());
  for (size_t i = 0; i < entries.size(); ++i) {
    CHECK(entries[i] == shaders[i] + ":c57.y=0");
  }
}

TEST_CASE("Motion blur off and car dirt off both apply, the road first", "[car_dirt]") {
  const std::vector<std::string> entries =
      Split(ComposePixelShaderConstantOverride(false, false, true), ';');
  REQUIRE(entries.size() == Split(DirtPixelShaderList(), ',').size() + 1);
  CHECK(entries.front() == kRoadEntry);
  CHECK(entries[1] == Split(DirtPixelShaderList(), ',').front() + ":c57.y=0");
}

TEST_CASE("The replay blur off gives its camera reprojection no motion", "[car_dirt]") {
  CHECK(ComposePixelShaderConstantOverride(true, true, false) ==
        "4746CAD32EF168CF:c0=1,0,0,0;4746CAD32EF168CF:c1=0,1,0,0;"
        "4746CAD32EF168CF:c2=0,0,1,0;4746CAD32EF168CF:c3=0,0,0,1");
}

TEST_CASE("The replay blur off comes after motion blur off", "[car_dirt]") {
  const std::vector<std::string> entries =
      Split(ComposePixelShaderConstantOverride(false, true, false), ';');
  REQUIRE(entries.size() == 5);
  CHECK(entries.front() == kRoadEntry);
  CHECK(entries[1] == "4746CAD32EF168CF:c0=1,0,0,0");
}

TEST_CASE("An override given at launch wins over the switches", "[car_dirt]") {
  CHECK_FALSE(SwitchesOwnOverride("B85EF04779ABD438:c57=1,1,0,0"));
  CHECK(SwitchesOwnOverride(""));
}

TEST_CASE("The dirt shader lists hold the scanned shaders", "[car_dirt]") {
  const std::vector<std::string> vertex = Split(DirtVertexShaderList(), ',');
  const std::vector<std::string> pixel = Split(DirtPixelShaderList(), ',');
  CHECK(vertex.size() == 8);
  CHECK(pixel.size() == 31);
  for (const std::string& hash : vertex) {
    CHECK(IsHash(hash));
  }
  for (const std::string& hash : pixel) {
    CHECK(IsHash(hash));
  }
}
