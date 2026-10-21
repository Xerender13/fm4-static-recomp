#include "input/prompt_swap.h"

#include <span>
#include <string_view>

#include <rex/hash.h>
#include <rex/logging.h>

#include "input/keycap.h"

namespace fm4_static_recomp::input {
namespace {

using rex::graphics::texture_replacement::RuntimeImage;

// The keycaps are drawn at 4x the icons' 32x32 (the texture cache's larger
// replacement path), with every level down to 1x1.
constexpr uint32_t kKeycapSize = 128;
constexpr uint32_t kKeycapLevels = 8;
// The pad's BACK circle ends at x 26 of 32 and the game draws its home icon
// right after it, so the BACK keycaps end there too.
constexpr uint32_t kBackKeycapWidth = kKeycapSize * 27 / 32;
// An .xds is a 52-byte D3D texture header, then the data as the game loads it.
constexpr size_t kXdsHeaderSize = 0x34;

std::vector<PadControl> ControlsOf(std::string_view name, InputContext context) {
  using enum PadControl;
  if (name == "A_Button_F3")
    return {kA};
  if (name == "B_Button_F3")
    return {kB};
  if (name == "X_Button_F3")
    return {kX};
  if (name == "Y_Button_F3")
    return {kY};
  if (name == "Back_Button_F3" || name == "Back_Button_D_F3")
    return {kBack};
  if (name == "Start_Button_F3")
    return {kStart};
  if (name == "D_Pad_Button_F3")
    return {kDpadUp, kDpadLeft, kDpadDown, kDpadRight};
  if (name == "D_Pad_Left_Right_F3")
    return {kDpadLeft, kDpadRight};
  if (name == "LTrigger_F3")
    return {kLeftTrigger};
  if (name == "RTrigger_F3")
    return {kRightTrigger};
  if (name == "RBumper_F3" || name == "RBumper_I")
    return {kRightShoulder};
  // While driving the left stick only steers.
  if (name == "L_Thumbstick_F3") {
    return context == InputContext::kDriving
               ? std::vector<PadControl>{kLStickLeft, kLStickRight}
               : std::vector<PadControl>{kLStickUp, kLStickLeft, kLStickDown, kLStickRight};
  }
  if (name == "RThumbstick_F3")
    return {kRStickUp, kRStickLeft, kRStickDown, kRStickRight};
  return {};
}

// The first key of each control, as the keycap shows it.
std::vector<std::string> Labels(const std::vector<PadControl>& controls, const BindingSet& set) {
  std::vector<std::string> labels;
  for (PadControl control : controls) {
    const std::string& bind = set.keys[size_t(control)];
    std::string label = KeyLabel(std::string_view(bind).substr(0, bind.find(',')));
    if (labels.empty() || labels.back() != label)
      labels.push_back(std::move(label));
  }
  return labels;
}

// The keycap and its smaller levels, each a 2x2 average (alpha-weighted).
std::shared_ptr<const RuntimeImage> KeycapImage(const Rgba& cap) {
  auto image = std::make_shared<RuntimeImage>();
  image->format = rex::graphics::xenos::TextureFormat::k_8_8_8_8;
  image->width = image->height = cap.size;
  image->levels = kKeycapLevels;
  image->data = cap.pixels;
  std::vector<uint8_t> level = cap.pixels;
  for (uint32_t size = cap.size / 2; size; size /= 2) {
    std::vector<uint8_t> smaller(size_t(size) * size * 4);
    for (uint32_t y = 0; y < size; ++y) {
      for (uint32_t x = 0; x < size; ++x) {
        float color = 0.0f, alpha = 0.0f;
        for (uint32_t dy = 0; dy < 2; ++dy) {
          for (uint32_t dx = 0; dx < 2; ++dx) {
            const uint8_t* p = &level[((size_t(y) * 2 + dy) * size * 2 + x * 2 + dx) * 4];
            color += p[0] * (p[3] / 255.0f);
            alpha += p[3] / 255.0f;
          }
        }
        uint8_t* const out = &smaller[(size_t(y) * size + x) * 4];
        out[0] = out[1] = out[2] = alpha > 0.0f ? uint8_t(std::lround(color / alpha)) : 0;
        out[3] = uint8_t(std::lround(alpha / 4.0f * 255.0f));
      }
    }
    image->data.insert(image->data.end(), smaller.begin(), smaller.end());
    level = std::move(smaller);
  }
  return image;
}

}  // namespace

std::vector<PromptTexture> LoadPromptTextures(const hostui::UiArchive& textures) {
  std::vector<PromptTexture> out;
  for (const char* name : kPromptTextureNames) {
    const auto data = textures.Read(std::string("UI4/") + name + ".xds");
    if (!data || data->size() <= kXdsHeaderSize) {
      REXLOG_WARN("Prompt icons: cannot read {}", name);
      continue;
    }
    // The icons have one level, so the data is the base level the cache hashes.
    out.push_back(
        {name, XXH3_64bits(data->data() + kXdsHeaderSize, data->size() - kXdsHeaderSize)});
  }
  return out;
}

PromptSwap::PromptSwap(std::vector<PromptTexture> textures, const hostui::VectorFont& font,
                       const Controls& controls, Registry registry)
    : textures_(std::move(textures)),
      registered_(textures_.size()),
      registry_(std::move(registry)) {
  for (InputContext context : {InputContext::kMenu, InputContext::kDriving}) {
    const BindingSet& set = context == InputContext::kDriving ? controls.driving : controls.menu;
    auto& keycaps = keycaps_[size_t(context)];
    for (const PromptTexture& texture : textures_) {
      const std::vector<std::string> labels = Labels(ControlsOf(texture.name, context), set);
      const bool back = texture.name == "Back_Button_F3" || texture.name == "Back_Button_D_F3";
      keycaps.push_back(KeycapImage(
          RenderKeycap(labels, kKeycapSize, font, back ? kBackKeycapWidth : kKeycapSize)));
    }
  }
}

void PromptSwap::Update(bool keyboard_active, InputContext context) {
  for (size_t i = 0; i < textures_.size(); ++i) {
    Image wanted = keyboard_active ? keycaps_[size_t(context)][i] : nullptr;
    if (wanted != registered_[i]) {
      registered_[i] = wanted;
      registry_(textures_[i].hash, std::move(wanted));
    }
  }
}

}  // namespace fm4_static_recomp::input
