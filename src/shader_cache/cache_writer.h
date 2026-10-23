#pragma once

#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <optional>
#include <string>
#include <vector>

#include "shader_cache/disc_shaders.h"

namespace fm4_static_recomp::shader_cache {

// The SDK's pipeline storage version the shipped list was made with:
// max(PipelineDescription::kVersion, SpirvShaderTranslator::Modification::kVersion).
// tools/shader_list.py check-sdk compares the three after a re-pin.
constexpr uint32_t kPipelineStorageVersion = 0x20261018;

struct CacheWriteResult {
  size_t shaders_needed = 0;
  size_t shaders_written = 0;
  size_t shaders_missing = 0;
  size_t pipelines_listed = 0;
  size_t pipelines_added = 0;
  // The shipped list was made for another SDK pipeline storage version than
  // the tool, or the player's pipelines for a newer one (cache_version).
  bool pipelines_skipped = false;
  uint32_t list_version = 0;
  uint32_t cache_version = 0;
};

// Adds to the SDK's shareable shader storage (4D530910.xsh and
// 4D530910.fbo.vk.xpso in shareable_dir) every shader the shipped list in
// list_dir names that the disc provides, and every listed pipeline. Records
// already there are kept and not written again. nullopt when a file cannot be
// read or written; error then names it.
std::optional<CacheWriteResult> WriteShaderCache(const std::vector<DiscShader>& disc,
                                                 const std::filesystem::path& list_dir,
                                                 const std::filesystem::path& shareable_dir,
                                                 std::string* error);

}  // namespace fm4_static_recomp::shader_cache
