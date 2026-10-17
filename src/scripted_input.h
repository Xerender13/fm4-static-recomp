#pragma once

#include <functional>
#include <string>

#include <rex/runtime.h>

namespace rex::ui {
class Presenter;
}

namespace fm4_static_recomp {

// When FM4_STATIC_RECOMP_INPUT_SCRIPT names a script, replaces the input system with
// one synthetic gamepad that plays it, so a route runs without window focus.
// Each line is "<start seconds> <duration seconds> <control>..." from process
// start; controls are A B X Y START BACK UP DOWN LEFT RIGHT LB RB LS RS and
// RT= LT= (0..1), LX= LY= RX= RY= (-1..1). '#' starts a comment. The file is
// re-read when it changes, so steps can be appended while the game runs. A
// line "@<name>" makes the times of the lines after it count from an input
// event (MarkInputEvent); a bare "@" goes back to process start.
void ConfigureScriptedInput(rex::RuntimeConfig& config);

// Marks a point in the game that script lines after "@<name>" count their time
// from (the nth such line follows the nth mark), and that a recording started
// through FM4_STATIC_RECOMP_INPUT_RECORD restarts its time base at.
void MarkInputEvent(const std::string& name);

// With a script active, writes the guest output as a BMP whenever a file named
// "<script>.shot" appears holding the destination path. The capture does not
// depend on the window being visible.
void StartCaptureWatcher(std::function<rex::ui::Presenter*()> presenter);

}  // namespace fm4_static_recomp
