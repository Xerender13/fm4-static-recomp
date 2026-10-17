#pragma once

#include <cstdint>
#include <functional>
#include <memory>
#include <optional>
#include <string>
#include <utility>
#include <vector>

namespace fm4_static_recomp::hostui {

enum class NavCommand {
  kUp,
  kDown,
  kLeft,
  kRight,
  kAccept,
  kBack,
  kClose,  // leave every screen (Start, like the title's pause menu)
};

// A whole number in an inclusive range, drawn as the game's tuning slider.
struct MenuSlider {
  // Drawn at the ends of the slider section's header.
  std::function<std::pair<int, int>()> range;
  std::function<int()> get;
  std::function<void(int value)> set;
};

// One row of a host menu: a label and a value beside it, changed with
// `adjust` (-1 or +1), or a slider stepped by one. The focused row's
// description, note and badge show in the description panel.
struct MenuRow {
  std::string label;
  std::function<std::string()> value;
  std::function<void(int direction)> adjust;
  std::optional<MenuSlider> slider;
  std::function<bool()> enabled;
  std::string description;
  // Current state worth knowing, or empty.
  std::function<std::string()> note;
  // Where the value comes from when not the page, or empty.
  std::function<std::string()> badge;
  // A change that is saved but not live yet.
  std::function<bool()> pending;
  // A thin line above the row: it starts a block of rows.
  bool divider_above = false;

  bool is_enabled() const { return !enabled || enabled(); }
};

class MenuScreen {
 public:
  MenuScreen(std::string title, std::vector<MenuRow> rows);

  const std::string& title() const { return title_; }
  const std::vector<MenuRow>& rows() const { return rows_; }
  // Called on every drawn frame, and with closing set when the screen leaves.
  void set_update(std::function<void(bool closing)> update) { update_ = std::move(update); }
  void Update(bool closing) {
    if (update_) {
      update_(closing);
    }
  }
  size_t focus() const { return focus_; }
  // Moves the focus to `index` if that row is enabled.
  bool SetFocus(size_t index);

  enum class Result { kNone, kChanged, kBack };
  // Applies a navigation command: up and down move the focus over enabled
  // rows with wrap-around, left and right adjust the focused row, accept
  // steps it forward, back leaves the screen.
  Result Handle(NavCommand command);
  // Moves the focused slider by `steps`, within its range.
  Result Slide(int steps);
  // Sets the focused slider to the value at `fraction` (0..1) of its range.
  Result SlideTo(float fraction);

 private:
  std::string title_;
  std::vector<MenuRow> rows_;
  std::function<void(bool closing)> update_;
  size_t focus_ = 0;
};

// Turns polled pad state into navigation commands with the usual menu key
// repeat. Buttons use the XINPUT_GAMEPAD bit values.
class PadNavigator {
 public:
  static constexpr uint16_t kDpadUp = 0x0001;
  static constexpr uint16_t kDpadDown = 0x0002;
  static constexpr uint16_t kDpadLeft = 0x0004;
  static constexpr uint16_t kDpadRight = 0x0008;
  static constexpr uint16_t kStart = 0x0010;
  static constexpr uint16_t kButtonA = 0x1000;
  static constexpr uint16_t kButtonB = 0x2000;
  static constexpr int16_t kStickThreshold = 16000;
  static constexpr uint64_t kRepeatDelayMs = 400;
  static constexpr uint64_t kRepeatIntervalMs = 110;

  // Returns the commands for this poll. Directions (d-pad or left stick)
  // repeat while held; A, B and Start fire once per press. The first poll after
  // Reset only records what is held, so the press that opened the UI does
  // not also act inside it. With `stick_x_free` the stick's left and right
  // are left to the caller.
  std::vector<NavCommand> Poll(uint16_t buttons, int16_t thumb_lx, int16_t thumb_ly,
                               uint64_t now_ms, bool stick_x_free = false);
  void Reset() { primed_ = false; }

 private:
  bool primed_ = false;
  uint16_t previous_buttons_ = 0;
  int held_direction_ = -1;
  uint64_t next_repeat_ms_ = 0;
};

}  // namespace fm4_static_recomp::hostui
