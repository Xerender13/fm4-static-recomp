#include <catch2/catch_test_macros.hpp>

#include "shadow_map.h"

using fm4_static_recomp::GroundShadowSize;
using fm4_static_recomp::ShadowMapSize;

TEST_CASE("Automatic shadow map size is high only for a 1x frame a temporal upscaler takes",
          "[shadow_map]") {
  CHECK(ShadowMapSize(0, 1, true) == 768);
  CHECK(ShadowMapSize(0, 1, false) == 512);
  CHECK(ShadowMapSize(0, 2, true) == 512);
  CHECK(ShadowMapSize(0, 4, true) == 512);
}

TEST_CASE("A shadow map size that was set wins over the automatic one", "[shadow_map]") {
  CHECK(ShadowMapSize(512, 1, true) == 512);
  CHECK(ShadowMapSize(768, 2, false) == 768);
  CHECK(ShadowMapSize(1024, 1, false) == 768);
  CHECK(ShadowMapSize(256, 1, true) == 512);
}

TEST_CASE("Automatic ground shadow size is high only for a 1x frame a temporal upscaler takes",
          "[shadow_map]") {
  CHECK(GroundShadowSize(0, 1, true) == 512);
  CHECK(GroundShadowSize(0, 1, false) == 256);
  CHECK(GroundShadowSize(0, 2, true) == 256);
}

TEST_CASE("A ground shadow size that was set wins over the automatic one", "[shadow_map]") {
  CHECK(GroundShadowSize(256, 1, true) == 256);
  CHECK(GroundShadowSize(768, 2, false) == 768);
  CHECK(GroundShadowSize(1024, 1, false) == 768);
  CHECK(GroundShadowSize(128, 1, false) == 256);
}
