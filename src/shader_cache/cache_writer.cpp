#include "shader_cache/cache_writer.h"

#include <charconv>
#include <cstdio>
#include <cstring>
#include <set>
#include <span>
#include <string_view>
#include <unordered_map>
#include <unordered_set>

#include <rex/filesystem.h>
#include <rex/hash.h>

namespace fm4_static_recomp::shader_cache {
namespace {

// The SDK's formats (src/graphics/vulkan/pipeline_cache.cpp, InitializeShaderStorage).
constexpr uint32_t kShaderStorageMagic = 0x48534558;  // 'XESH'
constexpr uint32_t kShaderStorageVersion = 0x20201219;
constexpr uint32_t kPipelineStorageMagic = 0x53504558;  // 'XEPS'
constexpr size_t kShaderHeaderSize = 12;
constexpr size_t kPipelineHeaderSize = 12;
constexpr size_t kPipelineRecordSize = 68;
constexpr size_t kPipelineDescriptionSize = kPipelineRecordSize - 8;
constexpr std::string_view kShaderFile = "4D530910.xsh";
constexpr std::string_view kPipelineFile = "4D530910.fbo.vk.xpso";
constexpr std::string_view kTableFile = "4D530910.vsfetch.tsv";
constexpr std::string_view kTableHeader = "# fm4 vertex fetch table 1";
constexpr uint32_t kNopInstruction[3] = {0xC8000000, 0x00000000, 0x02000000};

uint32_t Load32(const uint8_t* data) {
  uint32_t value;
  std::memcpy(&value, data, sizeof(value));
  return value;
}

uint64_t Load64(const uint8_t* data) {
  uint64_t value;
  std::memcpy(&value, data, sizeof(value));
  return value;
}

uint32_t LoadBig32(const uint8_t* data) {
  return uint32_t(data[0]) << 24 | uint32_t(data[1]) << 16 | uint32_t(data[2]) << 8 | data[3];
}

void StoreBig32(uint8_t* data, uint32_t value) {
  data[0] = uint8_t(value >> 24);
  data[1] = uint8_t(value >> 16);
  data[2] = uint8_t(value >> 8);
  data[3] = uint8_t(value);
}

template <typename T>
void Append(std::vector<uint8_t>& out, T value) {
  const auto* bytes = reinterpret_cast<const uint8_t*>(&value);
  out.insert(out.end(), bytes, bytes + sizeof(value));
}

// nullopt when the file exists but cannot be read (another process holds it)
// or cannot be looked up; an empty vector when it does not exist.
std::optional<std::vector<uint8_t>> ReadWhole(const std::filesystem::path& path) {
  std::error_code error;
  if (!std::filesystem::exists(path, error)) {
    if (error) {
      return std::nullopt;
    }
    return std::vector<uint8_t>();
  }
  FILE* file = rex::filesystem::OpenFile(path, "rb");
  if (!file) {
    return std::nullopt;
  }
  std::vector<uint8_t> data;
  uint8_t buffer[65536];
  for (size_t read; (read = fread(buffer, 1, sizeof(buffer), file)) != 0;) {
    data.insert(data.end(), buffer, buffer + read);
  }
  const bool failed = ferror(file) != 0;
  fclose(file);
  if (failed) {
    return std::nullopt;
  }
  return data;
}

// Keeps the first valid_size bytes of the file, then appends tail; with
// valid_size 0 the file is rewritten from tail alone.
bool WriteTail(const std::filesystem::path& path, uint64_t valid_size, uint64_t file_size,
               std::span<const uint8_t> tail) {
  if (valid_size == file_size && tail.empty()) {
    return true;
  }
  FILE* file = rex::filesystem::OpenFile(path, valid_size ? "r+b" : "wb");
  if (!file) {
    return false;
  }
  bool ok = true;
  if (valid_size) {
    ok = (valid_size == file_size || rex::filesystem::TruncateStdioFile(file, valid_size)) &&
         rex::filesystem::Seek(file, int64_t(valid_size), SEEK_SET);
  }
  ok = ok && fwrite(tail.data(), 1, tail.size(), file) == tail.size();
  return fclose(file) == 0 && ok;
}

struct TableEntry {
  uint64_t source = 0;
  uint32_t dword_count = 0;
  std::vector<std::pair<uint32_t, uint32_t>> xors;
  std::vector<uint32_t> nops;
};

template <typename T>
bool ParseNumber(std::string_view text, T& value, int base) {
  const auto [end, error] = std::from_chars(text.data(), text.data() + text.size(), value, base);
  return error == std::errc() && end == text.data() + text.size();
}

std::vector<std::string_view> Split(std::string_view text, char separator) {
  std::vector<std::string_view> parts;
  if (text.empty()) {
    return parts;
  }
  for (size_t start = 0;;) {
    const size_t end = text.find(separator, start);
    parts.push_back(text.substr(start, end - start));
    if (end == std::string_view::npos) {
      return parts;
    }
    start = end + 1;
  }
}

// Malformed lines are dropped: their shaders then count as missing.
std::optional<std::unordered_map<uint64_t, TableEntry>> ParseTable(std::string_view text) {
  std::vector<std::string_view> lines = Split(text, '\n');
  // A git checkout on Windows may turn the shipped file's line ends into CRLF.
  for (std::string_view& line : lines) {
    if (line.ends_with('\r')) {
      line.remove_suffix(1);
    }
  }
  if (lines.empty() || lines[0] != kTableHeader) {
    return std::nullopt;
  }
  std::unordered_map<uint64_t, TableEntry> table;
  for (size_t i = 1; i < lines.size(); ++i) {
    const std::vector<std::string_view> fields = Split(lines[i], '\t');
    uint64_t runtime;
    TableEntry entry;
    if (fields.size() != 5 || !ParseNumber(fields[0], runtime, 16) ||
        !ParseNumber(fields[1], entry.source, 16) || !ParseNumber(fields[2], entry.dword_count, 10)) {
      continue;
    }
    bool valid = true;
    for (std::string_view field : Split(fields[3], ',')) {
      const size_t colon = field.find(':');
      uint32_t index, value;
      valid = valid && colon != std::string_view::npos &&
              ParseNumber(field.substr(0, colon), index, 10) &&
              ParseNumber(field.substr(colon + 1), value, 16) && index < entry.dword_count;
      if (valid) {
        entry.xors.emplace_back(index, value);
      }
    }
    for (std::string_view field : Split(fields[4], ',')) {
      uint32_t instruction;
      valid = valid && ParseNumber(field, instruction, 10) &&
              uint64_t(instruction) * 3 + 3 <= entry.dword_count;
      if (valid) {
        entry.nops.push_back(instruction);
      }
    }
    if (valid) {
      table.insert_or_assign(runtime, std::move(entry));
    }
  }
  return table;
}

std::optional<std::vector<uint8_t>> Rebuild(const TableEntry& entry, const DiscShader& source) {
  if (source.pixel || source.ucode.size() != size_t(entry.dword_count) * 4) {
    return std::nullopt;
  }
  std::vector<uint8_t> ucode = source.ucode;
  for (const auto& [index, value] : entry.xors) {
    StoreBig32(&ucode[size_t(index) * 4], LoadBig32(&ucode[size_t(index) * 4]) ^ value);
  }
  for (uint32_t instruction : entry.nops) {
    for (size_t word = 0; word < 3; ++word) {
      StoreBig32(&ucode[(size_t(instruction) * 3 + word) * 4], kNopInstruction[word]);
    }
  }
  return ucode;
}

// The hashes of the valid records and the size they end at, as the SDK reads
// the file: a bad header means no valid bytes, a corrupt record ends the file.
uint64_t ReadShaderStorage(std::span<const uint8_t> data, std::unordered_set<uint64_t>& hashes) {
  if (data.size() < 8 || Load32(data.data()) != kShaderStorageMagic ||
      LoadBig32(data.data() + 4) != kShaderStorageVersion) {
    return 0;
  }
  uint64_t offset = 8;
  while (data.size() - offset >= kShaderHeaderSize) {
    const uint64_t hash = Load64(&data[offset]);
    const uint64_t size = uint64_t(Load32(&data[offset + 8]) & 0x7FFFFFFF) * 4;
    if (data.size() - offset - kShaderHeaderSize < size ||
        XXH3_64bits(&data[offset + kShaderHeaderSize], size) != hash) {
      break;
    }
    hashes.insert(hash);
    offset += kShaderHeaderSize + size;
  }
  return offset;
}

uint64_t ReadPipelineRecords(std::span<const uint8_t> data, std::unordered_set<uint64_t>& hashes) {
  uint64_t offset = kPipelineHeaderSize;
  while (data.size() - offset >= kPipelineRecordSize) {
    const uint64_t hash = Load64(&data[offset]);
    if (XXH3_64bits(&data[offset + 8], kPipelineDescriptionSize) != hash) {
      break;
    }
    hashes.insert(hash);
    offset += kPipelineRecordSize;
  }
  return offset;
}

}  // namespace

std::optional<CacheWriteResult> WriteShaderCache(const std::vector<DiscShader>& disc,
                                                 const std::filesystem::path& list_dir,
                                                 const std::filesystem::path& shareable_dir,
                                                 std::string* error) {
  auto fail = [error](std::string_view what, const std::filesystem::path& path) {
    if (error) {
      *error = std::string(what) + " " + rex::path_to_utf8(path);
    }
    return std::nullopt;
  };
  const std::filesystem::path list_path =
      rex::filesystem::ExtendedLengthPath(list_dir / kPipelineFile);
  const std::filesystem::path table_path = rex::filesystem::ExtendedLengthPath(list_dir / kTableFile);
  const std::filesystem::path shader_path =
      rex::filesystem::ExtendedLengthPath(shareable_dir / kShaderFile);
  const std::filesystem::path pipeline_path =
      rex::filesystem::ExtendedLengthPath(shareable_dir / kPipelineFile);

  const auto list = ReadWhole(list_path);
  if (!list || list->size() < kPipelineHeaderSize || Load32(list->data()) != kPipelineStorageMagic ||
      Load32(list->data() + 4) != 0 || (list->size() - kPipelineHeaderSize) % kPipelineRecordSize) {
    return fail("cannot read the shipped pipeline list", list_path);
  }
  const auto table_bytes = ReadWhole(table_path);
  const auto table =
      table_bytes ? ParseTable(std::string_view(reinterpret_cast<const char*>(table_bytes->data()),
                                                table_bytes->size()))
                  : std::nullopt;
  if (!table) {
    return fail("cannot read the vertex shader table", table_path);
  }

  CacheWriteResult result;
  result.list_version = LoadBig32(list->data() + 8);
  result.pipelines_listed = (list->size() - kPipelineHeaderSize) / kPipelineRecordSize;
  result.pipelines_skipped = result.list_version != kPipelineStorageVersion;

  // Ordered so that the file's layout does not depend on hashing.
  std::set<std::pair<uint64_t, bool>> needed;
  for (size_t i = 0; i < result.pipelines_listed; ++i) {
    const uint8_t* record = list->data() + kPipelineHeaderSize + i * kPipelineRecordSize;
    needed.emplace(Load64(record + 8), false);
    if (const uint64_t pixel_hash = Load64(record + 24)) {
      needed.emplace(pixel_hash, true);
    }
  }
  result.shaders_needed = needed.size();

  std::unordered_map<uint64_t, const DiscShader*> by_hash;
  for (const DiscShader& shader : disc) {
    by_hash.emplace(shader.hash, &shader);
  }

  std::error_code directory_error;
  std::filesystem::create_directories(rex::filesystem::ExtendedLengthPath(shareable_dir),
                                      directory_error);
  const auto shader_storage = ReadWhole(shader_path);
  if (!shader_storage) {
    return fail("cannot read", shader_path);
  }
  std::unordered_set<uint64_t> stored_shaders;
  const uint64_t shader_valid = ReadShaderStorage(*shader_storage, stored_shaders);
  std::vector<uint8_t> shader_tail;
  if (!shader_valid) {
    Append(shader_tail, kShaderStorageMagic);
    shader_tail.resize(shader_tail.size() + 4);
    StoreBig32(&shader_tail[shader_tail.size() - 4], kShaderStorageVersion);
  }
  for (const auto& [hash, pixel] : needed) {
    std::optional<std::vector<uint8_t>> ucode;
    if (pixel) {
      const auto found = by_hash.find(hash);
      if (found != by_hash.end() && found->second->pixel) {
        ucode = found->second->ucode;
      }
    } else if (const auto entry = table->find(hash); entry != table->end()) {
      if (const auto source = by_hash.find(entry->second.source); source != by_hash.end()) {
        ucode = Rebuild(entry->second, *source->second);
      }
    }
    if (!ucode || XXH3_64bits(ucode->data(), ucode->size()) != hash) {
      ++result.shaders_missing;
      continue;
    }
    if (stored_shaders.contains(hash)) {
      continue;
    }
    Append(shader_tail, hash);
    Append(shader_tail, uint32_t(ucode->size() / 4) | (pixel ? 0x80000000u : 0u));
    shader_tail.insert(shader_tail.end(), ucode->begin(), ucode->end());
    ++result.shaders_written;
  }
  if (!WriteTail(shader_path, shader_valid, shader_storage->size(), shader_tail)) {
    return fail("cannot write", shader_path);
  }

  if (result.pipelines_skipped) {
    return result;
  }
  const auto pipeline_storage = ReadWhole(pipeline_path);
  if (!pipeline_storage) {
    return fail("cannot read", pipeline_path);
  }
  const bool fbo_storage = pipeline_storage->size() >= kPipelineHeaderSize &&
                           Load32(pipeline_storage->data()) == kPipelineStorageMagic &&
                           Load32(pipeline_storage->data() + 4) == 0;
  // Written by a newer SDK than the list was made for (a re-pin without a new list): the
  // player's records are current, and the list's would be discarded at the next launch.
  if (fbo_storage && LoadBig32(pipeline_storage->data() + 8) > result.list_version) {
    result.pipelines_skipped = true;
    result.cache_version = LoadBig32(pipeline_storage->data() + 8);
    return result;
  }
  const bool same_header =
      fbo_storage && std::memcmp(pipeline_storage->data(), list->data(), kPipelineHeaderSize) == 0;
  std::unordered_set<uint64_t> stored_pipelines;
  const uint64_t pipeline_valid =
      same_header ? ReadPipelineRecords(*pipeline_storage, stored_pipelines) : 0;
  std::vector<uint8_t> pipeline_tail;
  if (!pipeline_valid) {
    pipeline_tail.assign(list->begin(), list->begin() + kPipelineHeaderSize);
  }
  for (size_t i = 0; i < result.pipelines_listed; ++i) {
    const uint8_t* record = list->data() + kPipelineHeaderSize + i * kPipelineRecordSize;
    if (stored_pipelines.insert(Load64(record)).second) {
      pipeline_tail.insert(pipeline_tail.end(), record, record + kPipelineRecordSize);
      ++result.pipelines_added;
    }
  }
  if (!WriteTail(pipeline_path, pipeline_valid, pipeline_storage->size(), pipeline_tail)) {
    return fail("cannot write", pipeline_path);
  }
  return result;
}

}  // namespace fm4_static_recomp::shader_cache
