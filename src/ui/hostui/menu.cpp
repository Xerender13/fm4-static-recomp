#include "ui/hostui/menu.h"

#include <algorithm>
#include <cmath>
#include <utility>

namespace fm4_static_recomp::hostui {

MenuScreen::MenuScreen(std::string title, std::vector<MenuRow> rows)
    : title_(std::move(title)), rows_(std::move(rows)) {
  for (size_t i = 0; i < rows_.size(); ++i) {
    if (rows_[i].is_enabled()) {
      focus_ = i;
      break;
    }
  }
}

bool MenuScreen::SetFocus(size_t index) {
  if (index >= rows_.size() || !rows_[index].is_enabled()) {
    return false;
  }
  focus_ = index;
  return true;
}

MenuScreen::Result MenuScreen::Handle(NavCommand command) {
  if (command == NavCommand::kBack || command == NavCommand::kClose) {
    return leave_ && !leave_() ? Result::kChanged : Result::kBack;
  }
  if (command == NavCommand::kApply) {
    if (!apply_) {
      return Result::kNone;
    }
    apply_();
    return Result::kChanged;
  }
  if (rows_.empty()) {
    return Result::kNone;
  }
  if (command == NavCommand::kUp || command == NavCommand::kDown) {
    const size_t count = rows_.size();
    size_t index = focus_;
    for (size_t step = 0; step < count; ++step) {
      index = command == NavCommand::kDown ? (index + 1) % count : (index + count - 1) % count;
      if (rows_[index].is_enabled()) {
        const bool moved = index != focus_;
        focus_ = index;
        return moved ? Result::kChanged : Result::kNone;
      }
    }
    return Result::kNone;
  }
  if (status().busy) {
    return Result::kNone;
  }
  const int direction = command == NavCommand::kLeft ? -1 : 1;
  if (rows_[focus_].slider) {
    return Slide(direction);
  }
  MenuRow& row = rows_[focus_];
  if (!row.is_enabled() || !row.adjust) {
    return Result::kNone;
  }
  row.adjust(direction);
  return Result::kChanged;
}

MenuScreen::Result MenuScreen::Slide(int steps) {
  if (focus_ >= rows_.size() || !rows_[focus_].slider || !rows_[focus_].is_enabled() ||
      status().busy) {
    return Result::kNone;
  }
  const MenuSlider& slider = *rows_[focus_].slider;
  const auto [low, high] = slider.range();
  const int current = slider.get();
  const int next = std::clamp(current + steps, low, high);
  if (next == current) {
    return Result::kNone;
  }
  slider.set(next);
  return Result::kChanged;
}

MenuScreen::Result MenuScreen::SlideTo(float fraction) {
  if (focus_ >= rows_.size() || !rows_[focus_].slider) {
    return Result::kNone;
  }
  const auto [low, high] = rows_[focus_].slider->range();
  const int target =
      low + int(std::lround(std::clamp(fraction, 0.0f, 1.0f) * float(high - low)));
  return Slide(target - rows_[focus_].slider->get());
}

std::vector<NavCommand> PadNavigator::Poll(uint16_t buttons, int16_t thumb_lx, int16_t thumb_ly,
                                           uint64_t now_ms, bool stick_x_free) {
  if (stick_x_free) {
    thumb_lx = 0;
  }
  int direction = -1;
  if ((buttons & kDpadUp) || thumb_ly > kStickThreshold) {
    direction = int(NavCommand::kUp);
  } else if ((buttons & kDpadDown) || thumb_ly < -kStickThreshold) {
    direction = int(NavCommand::kDown);
  } else if ((buttons & kDpadLeft) || thumb_lx < -kStickThreshold) {
    direction = int(NavCommand::kLeft);
  } else if ((buttons & kDpadRight) || thumb_lx > kStickThreshold) {
    direction = int(NavCommand::kRight);
  }

  std::vector<NavCommand> commands;
  if (!primed_) {
    primed_ = true;
    previous_buttons_ = buttons;
    held_direction_ = direction;
    next_repeat_ms_ = UINT64_MAX;  // a direction held at open waits for release
    return commands;
  }
  if (direction != held_direction_) {
    held_direction_ = direction;
    if (direction >= 0) {
      commands.push_back(NavCommand(direction));
      next_repeat_ms_ = now_ms + kRepeatDelayMs;
    }
  } else if (direction >= 0 && now_ms >= next_repeat_ms_) {
    commands.push_back(NavCommand(direction));
    next_repeat_ms_ = now_ms + kRepeatIntervalMs;
  }
  const uint16_t pressed = buttons & ~previous_buttons_;
  if (pressed & kButtonA) {
    commands.push_back(NavCommand::kAccept);
  }
  if (pressed & kButtonB) {
    commands.push_back(NavCommand::kBack);
  }
  if (pressed & kButtonY) {
    commands.push_back(NavCommand::kApply);
  }
  if ((pressed & kStart) && !(buttons & kBack)) {
    commands.push_back(NavCommand::kClose);
  }
  previous_buttons_ = buttons;
  return commands;
}

}  // namespace fm4_static_recomp::hostui
